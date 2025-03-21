# WP8.1-TOTP

這是一個在 Windows Phone 8.1 上運行的 TOTP (Time-based One-Time Password) 密碼產生器應用程式。這個專案的目的是在舊平台上提供一個簡單易用的雙重驗證解決方案。

## 功能

*   **TOTP 密碼生成：** 根據 RFC 6238 標準生成 TOTP 密碼。
*   **金鑰管理：** 支援新增、儲存、載入和刪除 TOTP 金鑰。
*   **時間校正：** 允許手動調整時間偏差，以應對時間同步問題。
*   **QR Code 掃描：** 支援通過掃描 QR Code 快速新增金鑰 (需要額外配置)。

## 使用方法

1.  **新增金鑰：** 點擊應用程式欄上的 "+" 按鈕，輸入金鑰 (Base32 格式)。
2.  **時間校正：** 如果生成的密碼不正確，請在 "校正" 頁面調整時間偏差。
3.  **查看密碼：** 在主選單中選擇金鑰，即可查看生成的 TOTP 密碼。

## QR Code 掃描 (需要額外配置)

這個應用程式支援通過掃描 QR Code 快速新增金鑰。但是，由於 Windows Phone 8.1 平台的限制，你需要自己架設一個 API 伺服器來處理 QR Code 解碼。
1.  **架設Docker伺服器：**
	*	請參見[[https://hub.docker.com/repository/docker/andyching168/fastapi-qr/general]([https://hub.docker.com/r/andyching168/fastapi-qr)](https://hub.docker.com/repository/docker/andyching168/fastapi-qr/general](https://hub.docker.com/r/andyching168/fastapi-qr))架設

2.  **修改應用程式程式碼：**

    *   修改 `MainPage.xaml.cs` 檔案中的 `qrDecodeAPI_URL` 變數，將其設定為你的 API 伺服器的 URL。

        ```csharp
        private const string qrDecodeAPI_URL = "http://example.com:8000/decode_qr"; // 請替換為你的 API 伺服器 URL
        ```

3.  **重新編譯並部署應用程式。**

**請注意：**

*   你需要自行負責 API 伺服器的安全性和可靠性。
*   這個專案只提供了一個基本的 QR Code 解碼功能，你可以根據自己的需求進行修改和擴展。

## 開源協議

這個專案使用 MIT License 授權條款。

## 貢獻

歡迎大家參與這個專案的開發！如果你有任何建議或意見，請提交 Issue 或 Pull Request。

## 作者

AndyChing168

## 感謝

感謝你使用 WP8.1-TOTP！
