# MailChecker

個人電子郵件小幫手 — 自動掃描 **Outlook + Gmail** 收件匣，把帳單信件搬到「帳單」資料夾／加上「帳單」標籤、標為已讀，並透過 LINE 通知該繳費了。

## 功能

- 同時支援 **Microsoft 帳號（Outlook）** 與 **Google 帳號（Gmail）**，兩邊同時掃描、平行處理。
- **第一次執行**：掃描收件匣全部信件並分類。
- **後續執行**：只處理上次執行後新到的信件（依 `receivedDateTime` / Gmail `after:` 過濾）。
- **判斷依據**：寄件人、標題、內文。**不檢查附件**（避免不必要的資安風險）。
- **命中帳單**：
  1. Outlook：搬到「帳單」資料夾；Gmail：加上「帳單」label 並移出 INBOX
  2. 標為已讀
  3. LINE 推播：寄件人 / 標題 / 收件時間 / 內文摘要
- 兩邊的處理狀態分開記錄在同一份 `state.json`（依 provider key 分組），不會互相干擾。

## 技術選型

| 元件 | 方案 |
| --- | --- |
| Outlook 整合 | Microsoft Graph API（OAuth Device Code Flow） |
| Gmail 整合 | Google Gmail API v1（OAuth Installed App，瀏覽器授權） |
| LINE 通知 | LINE Messaging API（push message） |
| 分類 | 關鍵字 + 寄件人白名單，設定在 `keywords.json` |
| Runtime | .NET 8 console |

> 兩邊任選一個或兩個都設定都可以。沒設定的 provider 會自動跳過。

## 設定步驟

### 1. （選用）註冊 Azure AD 應用程式（讀寫 Outlook 用）

1. 進入 [Azure Portal → App registrations](https://portal.azure.com/#blade/Microsoft_AAD_RegisteredApps) → **New registration**
2. Name：`MailChecker`（隨意）
3. Supported account types：依信箱類型選
   - Outlook.com / Hotmail：**Personal Microsoft accounts only**
   - 公司 / 學校：**Accounts in this organizational directory only**
   - 都要支援：**Accounts in any organizational directory and personal Microsoft accounts**
4. Redirect URI：留空
5. 註冊後：
   - **Overview** → 抄下 `Application (client) ID`
   - **Authentication** → 把 **Allow public client flows** 切到 **Yes** → Save
   - **API permissions** → Add a permission → Microsoft Graph → **Delegated permissions**：
     - `Mail.ReadWrite`
     - `User.Read`

### 2. （選用）建立 Google Cloud OAuth Client（讀寫 Gmail 用）

1. 進入 [Google Cloud Console](https://console.cloud.google.com/) → 建立或選擇一個專案
2. **APIs & Services → Library** → 啟用 **Gmail API**
3. **APIs & Services → OAuth consent screen**
   - User type：External（個人 Gmail）或 Internal（公司 Workspace）
   - 填基本資訊（App name、Support email、Developer contact）
   - **Scopes**：加入 `https://www.googleapis.com/auth/gmail.modify`
   - **Test users**：把自己 Gmail 加進去（External + 仍在 Testing 狀態時必要）
4. **APIs & Services → Credentials** → Create Credentials → **OAuth client ID**
   - Application type：**Desktop app**
   - 名稱：`MailChecker`（隨意）
   - 建立後抄下 **Client ID** 與 **Client secret**

> 第一次跑程式時會自動開瀏覽器要你登入授權，授權後 refresh token 會被存到 `data/gmail-token-cache/`，後續免登。
> 若在純 CLI server 上跑沒有瀏覽器，請先在桌機上跑一次完成授權，再把 `data/gmail-token-cache/` 整個目錄複製到 server。

### 3. 建立 LINE Messaging API Channel（推播通知用）

1. 進入 [LINE Developers Console](https://developers.line.biz/console/)
2. 建立或選一個 Provider → **Create a new channel** → **Messaging API**
3. 填完基本資料建立完成後：
   - **Messaging API** 分頁 → 抄下 **Channel access token**（如果沒有就按 Issue）
   - **Basic settings** 分頁 → 抄下 **Your user ID**
   - 把 channel 對應的 LINE 官方帳號加為好友（掃 QR Code），不然 push 會被退
4. webhook 可以關掉，我們只用 push API。

### 4. 設定 `appsettings.json`

```bash
cd src/MailChecker
cp appsettings.example.json appsettings.json
```

填入你拿到的值：

```json
{
  "Graph": {
    "ClientId": "Azure AD 的 Application (client) ID（不用就留空）",
    "TenantId": "common"
  },
  "Gmail": {
    "ClientId": "Google Cloud 的 OAuth Client ID（不用就留空）",
    "ClientSecret": "Google Cloud 的 OAuth Client Secret"
  },
  "Line": {
    "ChannelAccessToken": "LINE channel access token",
    "UserId": "你的 LINE userId"
  }
}
```

> - `appsettings.json` 已加入 `.gitignore`，不會被 commit。
> - 不想放在檔案裡也可以用環境變數，前綴 `MAILCHECKER_`，例如 `MAILCHECKER_Gmail__ClientSecret=...`、`MAILCHECKER_Line__ChannelAccessToken=...`。
> - 兩邊任設一個就會啟用該邊；兩邊都填就會同時掃描兩邊。

### 5. （可選）調整關鍵字

`src/MailChecker/keywords.json` 已內建一份台灣常見的帳單關鍵字 + 銀行 / 電信 / 水電 / 政府單位的寄件人域名白名單，可自行新增刪減。

## 執行

```bash
dotnet run --project src/MailChecker
```

- 第一次：
  - Microsoft：印出登入連結 + 8 碼 device code，去瀏覽器登入。
  - Gmail：自動開瀏覽器跳出 Google 同意畫面。
  - 兩邊的授權階段會「依序」進行，避免兩個登入提示同時搶 console。
- 授權完成後，兩邊的信件處理會「平行」進行。
- token 都會被快取（Outlook 用 Azure.Identity 的 token cache、Gmail 用 `data/gmail-token-cache/`），下次免登。
- state、token cache 都寫在 `src/MailChecker/bin/.../data/` 之下，這個資料夾也已被 gitignore。

## 排程

要每小時自動跑一次的話：

- **Linux / macOS**：cron `0 * * * * cd /path/to/MailChecker && dotnet run --project src/MailChecker >> mailchecker.log 2>&1`
- **Windows**：用「工作排程器」呼叫 `dotnet run --project src/MailChecker`

## 目錄

```
src/MailChecker/
├── Configuration/    # AppConfig、KeywordRules POCO
├── Models/           # MailItem、ClassificationResult
├── Services/
│   ├── IMailProvider.cs          # 兩邊共用的抽象介面
│   ├── GraphAuthProvider.cs      # Microsoft Device Code Flow
│   ├── GraphService.cs           # Outlook 操作（資料夾、移信、標已讀）
│   ├── GraphMailProvider.cs      # 包裝成 IMailProvider
│   ├── GmailAuthProvider.cs      # Google OAuth（瀏覽器授權）
│   ├── GmailService.cs           # Gmail 操作（label、移信、標已讀）— 同時實作 IMailProvider
│   ├── ClassifierService.cs      # 關鍵字 / 寄件人 / 域名分類
│   ├── LineMessagingService.cs   # LINE push（thread-safe，可被兩邊同時呼叫）
│   ├── StateService.cs           # 多 provider 的 state store
│   └── HtmlToText.cs
├── Program.cs                    # 主流程（auth 依序、處理平行）
├── appsettings.example.json
├── appsettings.json              # (gitignored — 你自己建)
└── keywords.json
```

## 安全須知

- 不下載 / 不掃描附件 — 純粹用寄件人、標題、純文字內文判斷。
- 內文 HTML 會先去掉 `<script>` / `<style>` 區塊再轉純文字才丟給分類器或 LINE。
- Channel access token、Azure AD client id、Google client secret 都不要寫進原始碼 — 用 `appsettings.json`（已被 gitignore）或環境變數。
- Gmail OAuth scope 用 `gmail.modify`（可以讀信、加 label、標已讀，但**不能永久刪除**），降低風險。
