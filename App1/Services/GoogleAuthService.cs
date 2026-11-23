using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Security.Authentication.Web;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Web.Http;

namespace App1.Services
{
    public sealed class GoogleAuthService
    {
        private const string Scope = "https://www.googleapis.com/auth/youtube.readonly";
        private static readonly Uri TokenUri = new Uri("https://oauth2.googleapis.com/token");
        private static readonly Uri DeviceCodeUri = new Uri("https://oauth2.googleapis.com/device/code");
        private readonly HttpClient _http = new HttpClient();
        private TaskCompletionSource<bool> _signInTask;
        private string _pendingRedirect;
        private string _lastTokenErrorCode;

        public event Action<string, string> DeviceCodeReceived;

        public string LastError { get; private set; }

        public bool IsSignedIn
        {
            get { return !string.IsNullOrEmpty(AccessToken); }
        }

        public string AccessToken
        {
            get { return GetSetting("GoogleAccessToken"); }
        }

        public string ClientId
        {
            get { return GetSetting("GoogleClientId"); }
            set { SetSetting("GoogleClientId", value ?? ""); }
        }

        public string ClientSecret
        {
            get { return GetSetting("GoogleClientSecret"); }
            set { SetSetting("GoogleClientSecret", value ?? ""); }
        }

        public string GetRedirectUri()
        {
            return WebAuthenticationBroker.GetCurrentApplicationCallbackUri().AbsoluteUri;
        }

        public async Task<bool> SignInAsync()
        {
            LastError = "";
            var clientId = ClientId;
            if (string.IsNullOrEmpty(clientId))
            {
                throw new Exception("Add a Google OAuth client ID in Settings first.");
            }

            if (string.IsNullOrEmpty(ClientSecret))
            {
                throw new Exception("Add your OAuth client secret in Settings.");
            }

            var device = await RequestDeviceCodeAsync();
            var userCode = JsonGetString(device, "user_code");
            var verificationUrl = JsonGetString(device, "verification_url");
            var deviceCode = JsonGetString(device, "device_code");
            if (string.IsNullOrEmpty(userCode) || string.IsNullOrEmpty(deviceCode))
            {
                LastError = "Google did not return a device sign-in code.";
                return false;
            }

            var interval = JsonGetInt(device, "interval", 5);
            var expiresIn = JsonGetInt(device, "expires_in", 600);
            if (DeviceCodeReceived != null)
            {
                DeviceCodeReceived(verificationUrl, userCode);
            }

            return await PollDeviceTokenAsync(deviceCode, interval, expiresIn);
        }

        private async Task<JsonObject> RequestDeviceCodeAsync()
        {
            var body = new Dictionary<string, string>();
            body["client_id"] = ClientId;
            body["client_secret"] = ClientSecret;
            body["scope"] = Scope;

            var text = await PostFormAsync(DeviceCodeUri, body);
            return JsonObject.Parse(text);
        }

        private async Task<bool> PollDeviceTokenAsync(string deviceCode, int intervalSeconds, int expiresIn)
        {
            var deadline = DateTime.UtcNow.AddSeconds(expiresIn > 0 ? expiresIn : 600);
            var interval = intervalSeconds > 0 ? intervalSeconds : 5;

            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(interval));

                var body = new Dictionary<string, string>();
                body["client_id"] = ClientId;
                body["client_secret"] = ClientSecret;
                body["device_code"] = deviceCode;
                body["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code";

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var request = new HttpRequestMessage(HttpMethod.Post, TokenUri);
                try
                {
                    request.Content = new HttpStringContent(
                        BuildForm(body),
                        UnicodeEncoding.Utf8,
                        "application/x-www-form-urlencoded");
                    string text;
                    using (var response = await _http.SendRequestAsync(request).AsTask(cts.Token))
                    {
                        text = await response.Content.ReadAsStringAsync();
                        if (response.IsSuccessStatusCode)
                        {
                            return StoreTokenResponse(text);
                        }
                    }

                    var errorCode = GetTokenErrorCode(text);
                    if (errorCode == "authorization_pending")
                    {
                        continue;
                    }

                    if (errorCode == "slow_down")
                    {
                        interval += 5;
                        continue;
                    }

                    LastError = ParseTokenError(text);
                    return false;
                }
                finally
                {
                    cts.Dispose();
                    request.Dispose();
                }
            }

            LastError = "Sign-in timed out. Enter the code on Google before it expires.";
            return false;
        }

        private async Task<string> PostFormAsync(Uri uri, Dictionary<string, string> fields)
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var request = new HttpRequestMessage(HttpMethod.Post, uri);
            try
            {
                request.Content = new HttpStringContent(
                    BuildForm(fields),
                    UnicodeEncoding.Utf8,
                    "application/x-www-form-urlencoded");
                using (var response = await _http.SendRequestAsync(request).AsTask(cts.Token))
                {
                    var text = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        LastError = ParseTokenError(text);
                        throw new Exception(LastError);
                    }

                    return text;
                }
            }
            finally
            {
                cts.Dispose();
                request.Dispose();
            }
        }

        private static string GetTokenErrorCode(string json)
        {
            try
            {
                var obj = JsonObject.Parse(json);
                return obj.ContainsKey("error") ? obj["error"].GetString() : "";
            }
            catch
            {
                return "";
            }
        }

        private static string JsonGetString(JsonObject obj, string key)
        {
            if (obj == null || !obj.ContainsKey(key))
            {
                return "";
            }

            if (obj[key].ValueType == JsonValueType.String)
            {
                return obj[key].GetString();
            }

            return "";
        }

        private static int JsonGetInt(JsonObject obj, string key, int fallback)
        {
            if (obj == null || !obj.ContainsKey(key) || obj[key].ValueType != JsonValueType.Number)
            {
                return fallback;
            }

            return (int)obj[key].GetNumber();
        }

        public async void CompleteSignIn(WebAuthenticationResult result)
        {
            if (_signInTask == null)
            {
                return;
            }

            try
            {
                LastError = "";
                if (result.ResponseStatus == WebAuthenticationStatus.UserCancel)
                {
                    LastError = "Sign-in was cancelled.";
                    _signInTask.TrySetResult(false);
                    return;
                }

                if (result.ResponseStatus != WebAuthenticationStatus.Success)
                {
                    LastError = "Google sign-in failed (" + result.ResponseStatus.ToString() + ").";
                    _signInTask.TrySetResult(false);
                    return;
                }

                var error = ExtractOAuthError(result.ResponseData);
                if (!string.IsNullOrEmpty(error))
                {
                    LastError = error;
                    _signInTask.TrySetResult(false);
                    return;
                }

                var code = ExtractCode(result.ResponseData);
                if (string.IsNullOrEmpty(code))
                {
                    LastError = "Google did not return an authorization code. Check the redirect URI in Google Cloud Console.";
                    _signInTask.TrySetResult(false);
                    return;
                }

                var ok = await ExchangeCodeAsync(code, _pendingRedirect ?? "");
                if (!ok && string.IsNullOrEmpty(LastError))
                {
                    LastError = "Could not exchange the sign-in code for a token.";
                }

                _signInTask.TrySetResult(ok);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _signInTask.TrySetResult(false);
            }
            finally
            {
                _signInTask = null;
                _pendingRedirect = "";
            }
        }

        public void SignOut()
        {
            SetSetting("GoogleAccessToken", "");
            SetSetting("GoogleRefreshToken", "");
            SetSetting("GoogleTokenExpiry", "");
            LastError = "";
        }

        public async Task<string> GetValidAccessTokenAsync()
        {
            var token = AccessToken;
            if (string.IsNullOrEmpty(token))
            {
                return "";
            }

            var expiryText = GetSetting("GoogleTokenExpiry");
            if (!string.IsNullOrEmpty(expiryText))
            {
                long expiry;
                if (long.TryParse(expiryText, out expiry))
                {
                    var now = UnixNow();
                    if (now >= expiry - 60)
                    {
                        if (!await RefreshTokenAsync())
                        {
                            return "";
                        }

                        return AccessToken;
                    }
                }
            }

            return token;
        }

        private async Task<bool> ExchangeCodeAsync(string code, string redirectUri)
        {
            var body = new Dictionary<string, string>();
            body["code"] = code;
            body["client_id"] = ClientId;
            body["redirect_uri"] = redirectUri;
            body["grant_type"] = "authorization_code";
            body["client_secret"] = ClientSecret;

            var json = await PostTokenAsync(body);
            return StoreTokenResponse(json);
        }

        private async Task<bool> RefreshTokenAsync()
        {
            var refresh = GetSetting("GoogleRefreshToken");
            if (string.IsNullOrEmpty(refresh))
            {
                return false;
            }

            var body = new Dictionary<string, string>();
            body["client_id"] = ClientId;
            body["refresh_token"] = refresh;
            body["grant_type"] = "refresh_token";
            body["client_secret"] = ClientSecret;

            try
            {
                var json = await PostTokenAsync(body);
                return StoreTokenResponse(json);
            }
            catch (Exception ex)
            {
                if (string.Equals(_lastTokenErrorCode, "invalid_grant", StringComparison.OrdinalIgnoreCase))
                {
                    // The refresh token itself is no longer usable: sign out so the
                    // app doesn't keep pretending to be signed in with a dead token.
                    SignOut();
                    LastError = "Your Google sign-in has expired. Please sign in again.";
                }
                else
                {
                    LastError = "Could not refresh the Google sign-in: " + ex.Message;
                }

                return false;
            }
        }

        private async Task<string> PostTokenAsync(Dictionary<string, string> fields)
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var request = new HttpRequestMessage(HttpMethod.Post, TokenUri);
            try
            {
                request.Content = new HttpStringContent(
                    BuildForm(fields),
                    UnicodeEncoding.Utf8,
                    "application/x-www-form-urlencoded");
                using (var response = await _http.SendRequestAsync(request).AsTask(cts.Token))
                {
                    var text = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        LastError = ParseTokenError(text);
                        _lastTokenErrorCode = GetTokenErrorCode(text);
                        throw new Exception(LastError);
                    }

                    return text;
                }
            }
            finally
            {
                cts.Dispose();
                request.Dispose();
            }
        }

        private static string ParseTokenError(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return "Google token request failed.";
            }

            try
            {
                var obj = JsonObject.Parse(json);
                var error = obj.ContainsKey("error") ? obj["error"].GetString() : "";
                var description = obj.ContainsKey("error_description") ? obj["error_description"].GetString() : "";
                if (!string.IsNullOrEmpty(description))
                {
                    return "Google: " + description;
                }

                if (!string.IsNullOrEmpty(error))
                {
                    return "Google: " + error;
                }
            }
            catch
            {
            }

            return "Google token error: " + json;
        }

        private bool StoreTokenResponse(string json)
        {
            try
            {
                var obj = JsonObject.Parse(json);
                var access = JsonGetString(obj, "access_token");
                if (string.IsNullOrEmpty(access))
                {
                    return false;
                }

                SetSetting("GoogleAccessToken", access);
                if (obj.ContainsKey("refresh_token") && obj["refresh_token"].ValueType == JsonValueType.String)
                {
                    SetSetting("GoogleRefreshToken", obj["refresh_token"].GetString());
                }

                var expiresIn = 3600L;
                if (obj.ContainsKey("expires_in") && obj["expires_in"].ValueType == JsonValueType.Number)
                {
                    expiresIn = (long)obj["expires_in"].GetNumber();
                }

                SetSetting("GoogleTokenExpiry", (UnixNow() + expiresIn).ToString());
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static long UnixNow()
        {
            return (long)(DateTimeOffset.UtcNow - new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalSeconds;
        }

        private static string ExtractOAuthError(string responseData)
        {
            if (string.IsNullOrEmpty(responseData))
            {
                return "";
            }

            var query = responseData;
            if (query.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                query = new Uri(query).Query;
            }

            if (query.StartsWith("?"))
            {
                query = query.Substring(1);
            }

            var error = "";
            var description = "";
            foreach (var part in query.Split('&'))
            {
                var pieces = part.Split('=');
                if (pieces.Length == 2 && pieces[0] == "error")
                {
                    error = Uri.UnescapeDataString(pieces[1]);
                }

                if (pieces.Length == 2 && pieces[0] == "error_description")
                {
                    description = Uri.UnescapeDataString(pieces[1]);
                }
            }

            if (!string.IsNullOrEmpty(description))
            {
                return "Google: " + description;
            }

            if (!string.IsNullOrEmpty(error))
            {
                return "Google: " + error;
            }

            return "";
        }

        private static string ExtractCode(string responseData)
        {
            if (string.IsNullOrEmpty(responseData))
            {
                return "";
            }

            var trimmed = responseData.Trim();
            if (trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(trimmed);
                var query = uri.Query;
                if (query.StartsWith("?"))
                {
                    query = query.Substring(1);
                }

                foreach (var part in query.Split('&'))
                {
                    var pieces = part.Split('=');
                    if (pieces.Length == 2 && pieces[0] == "code")
                    {
                        return Uri.UnescapeDataString(pieces[1]);
                    }
                }

                return "";
            }

            if (trimmed.StartsWith("code=", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(trimmed.Substring(5));
            }

            return trimmed;
        }

        private static string BuildForm(Dictionary<string, string> fields)
        {
            var parts = new List<string>();
            foreach (var pair in fields)
            {
                parts.Add(Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value ?? ""));
            }

            return string.Join("&", parts);
        }

        private static string GetSetting(string key)
        {
            var value = ApplicationData.Current.LocalSettings.Values[key];
            return value as string ?? "";
        }

        private static void SetSetting(string key, string value)
        {
            ApplicationData.Current.LocalSettings.Values[key] = value ?? "";
        }
    }
}
