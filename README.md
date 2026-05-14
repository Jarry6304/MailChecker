# MailChecker

個人電子郵件小幫手 — 自動掃描 Outlook 收件匣，把帳單信件搬到「帳單」資料夾、標為已讀，並透過 LINE 通知該繳費了。

## 功能

- **第一次執行**：掃描收件匣全部信件並分類。
- **後續執行**：只處理上次執行後新到的信件（依 `receivedDateTime` 過濾）。
- **判斷依據**：寄件人、標題、內文。**不檢查附件**（避免不必要的資安風險）。
- **命中帳單**：
  1. 在 Outlook 建立 / 使用「帳單」資料夾，把信搬進去
  2. 把信標為已讀
  3. LINE 推播：寄件人 / 標題 / 收件時間 / 內文摘要

## 技術選型

| 元件 | 方案 |
| --- | --- |
| Outlook 整合 | Microsoft Graph API（跨平台，OAuth 委派權限） |
| Auth flow | Device Code Flow（首次跳出登入連結，後續用 cached token） |
| LINE 通知 | LINE Messaging API（push message） |
| 分類 | 關鍵字 + 寄件人白名單，設定在 `keywords.json` |
| Runtime | .NET 8 console |

## 設定步驟

### 1. 註冊 Azure AD 應用程式（讀寫 Outlook 信件用）

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
   - （個人帳號自動同意；公司 / 學校帳號可能需要管理員同意）

### 2. 建立 LINE Messaging API Channel（推播通知用）

1. 進入 [LINE Developers Console](https://developers.line.biz/console/)
2. 建立或選一個 Provider → **Create a new channel** → **Messaging API**
3. 填完基本資料建立完成後：
   - **Messaging API** 分頁 → 抄下 **Channel access token**（如果沒有就按 Issue）
   - **Basic settings** 分頁 → 抄下 **Your user ID**（在 LINE Official Account Manager → Gain Friends → 也可以從 webhook 收到自己的 `userId`）
   - 把 channel 對應的 LINE 官方帳號加為好友（在 Messaging API 分頁掃 QR Code），不然 push 會被退
4. 把 webhook 關掉沒關係，我們只用 push API。

### 3. 設定 `appsettings.json`

```bash
cd src/MailChecker
cp appsettings.example.json appsettings.json
```

填入：

```json
{
  "Graph": {
    "ClientId": "從 Azure AD 拿到的 Application (client) ID",
    "TenantId": "common"
  },
  "Line": {
    "ChannelAccessToken": "從 LINE Developers 拿到的 long-lived channel access token",
    "UserId": "你自己的 LINE userId（U 開頭，33 chars）"
  }
}
```

> `appsettings.json` 已加入 `.gitignore`，不會被 commit。
> 不想放在檔案裡也可以用環境變數，前綴 `MAILCHECKER_`，例如 `MAILCHECKER_Line__ChannelAccessToken=...`。

### 4. （可選）調整關鍵字

`src/MailChecker/keywords.json` 已內建一份台灣常見的帳單關鍵字 + 銀行 / 電信 / 水電 / 政府單位的寄件人域名白名單，可自行新增刪減。

## 執行

```bash
dotnet run --project src/MailChecker
```

- 第一次：會印出一段 Microsoft 登入連結 + 8 碼 code，去瀏覽器登入後回來就會繼續。
- token 會被快取（Azure.Identity 的 `TokenCachePersistenceOptions`），下次免登。
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
├── Services/         # GraphAuthProvider、GraphService、ClassifierService、LineMessagingService、StateService、HtmlToText
├── Program.cs        # 主流程
├── appsettings.example.json
├── appsettings.json  # (gitignored — 你自己建)
└── keywords.json
```

## 安全須知

- 不下載 / 不掃描附件 — 純粹用寄件人、標題、純文字內文判斷。
- 內文 HTML 會先去掉 `<script>` / `<style>` 區塊再轉純文字才丟給分類器或 LINE。
- Channel access token、Azure AD client id 不要寫進原始碼 — 用 `appsettings.json`（已被 gitignore）或環境變數。
