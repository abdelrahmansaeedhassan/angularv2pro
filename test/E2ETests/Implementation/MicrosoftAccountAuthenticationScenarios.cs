using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNet.Http.Internal;
using Microsoft.AspNet.WebUtilities;
using Microsoft.Extensions.Logging;
using Xunit;

namespace E2ETests
{
    public partial class Validator
    {
        public async Task LoginWithMicrosoftAccount()
        {
            _httpClientHandler = new HttpClientHandler() { AllowAutoRedirect = false };
            _httpClient = new HttpClient(_httpClientHandler) { BaseAddress = new Uri(_deploymentResult.ApplicationBaseUri) };

            var response = await _httpClient.GetAsync("Account/Login");
            await ThrowIfResponseStatusNotOk(response);
            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Signing in with Microsoft account");
            var formParameters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("provider", "Microsoft"),
                new KeyValuePair<string, string>("returnUrl", "/"),
                new KeyValuePair<string, string>("__RequestVerificationToken", HtmlDOMHelper.RetrieveAntiForgeryToken(responseContent, "/Account/ExternalLogin")),
            };

            var content = new FormUrlEncodedContent(formParameters.ToArray());
            response = await _httpClient.PostAsync("Account/ExternalLogin", content);
            Assert.Equal<string>("https://login.live.com/oauth20_authorize.srf", response.Headers.Location.AbsoluteUri.Replace(response.Headers.Location.Query, string.Empty));
            var queryItems = new ReadableStringCollection(QueryHelpers.ParseQuery(response.Headers.Location.Query));
            Assert.Equal<string>("code", queryItems["response_type"]);
            Assert.Equal<string>("[ClientId]", queryItems["client_id"]);
            Assert.Equal<string>(_deploymentResult.ApplicationBaseUri + "signin-microsoft", queryItems["redirect_uri"]);
            Assert.Equal<string>("wl.basic wl.signin", queryItems["scope"]);
            Assert.Equal<string>("ValidStateData", queryItems["state"]);
            Assert.Equal<string>("custom", queryItems["custom_redirect_uri"]);

            //Check for the correlation cookie
            Assert.NotNull(_httpClientHandler.CookieContainer.GetCookies(new Uri(_deploymentResult.ApplicationBaseUri)).GetCookieWithName(".AspNet.Correlation.Microsoft"));

            //This is just to generate a correlation cookie. Previous step would generate this cookie, but we have reset the handler now.
            _httpClientHandler = new HttpClientHandler() { AllowAutoRedirect = true };
            _httpClient = new HttpClient(_httpClientHandler) { BaseAddress = new Uri(_deploymentResult.ApplicationBaseUri) };

            response = await _httpClient.GetAsync("Account/Login");
            responseContent = await response.Content.ReadAsStringAsync();
            formParameters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("provider", "Microsoft"),
                new KeyValuePair<string, string>("returnUrl", "/"),
                new KeyValuePair<string, string>("__RequestVerificationToken", HtmlDOMHelper.RetrieveAntiForgeryToken(responseContent, "/Account/ExternalLogin")),
            };

            content = new FormUrlEncodedContent(formParameters.ToArray());
            response = await _httpClient.PostAsync("Account/ExternalLogin", content);

            //Post a message to the MicrosoftAccount middleware
            response = await _httpClient.GetAsync("signin-microsoft?code=ValidCode&state=ValidStateData");
            await ThrowIfResponseStatusNotOk(response);
            responseContent = await response.Content.ReadAsStringAsync();

            //Correlation cookie not getting cleared after successful signin?
            if (!Helpers.RunningOnMono)
            {
                Assert.Null(_httpClientHandler.CookieContainer.GetCookies(new Uri(_deploymentResult.ApplicationBaseUri)).GetCookieWithName(".AspNet.Correlation.Microsoft"));
            }
            Assert.Equal(_deploymentResult.ApplicationBaseUri + "Account/ExternalLoginCallback?ReturnUrl=%2F", response.RequestMessage.RequestUri.AbsoluteUri);

            formParameters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Email", "microsoft@test.com"),
                new KeyValuePair<string, string>("__RequestVerificationToken", HtmlDOMHelper.RetrieveAntiForgeryToken(responseContent, "/Account/ExternalLoginConfirmation?ReturnUrl=%2F")),
            };

            content = new FormUrlEncodedContent(formParameters.ToArray());
            response = await _httpClient.PostAsync("Account/ExternalLoginConfirmation", content);
            await ThrowIfResponseStatusNotOk(response);
            responseContent = await response.Content.ReadAsStringAsync();

            Assert.Contains(string.Format("Hello {0}!", "microsoft@test.com"), responseContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Log off", responseContent, StringComparison.OrdinalIgnoreCase);
            //Verify cookie sent
            Assert.NotNull(_httpClientHandler.CookieContainer.GetCookies(new Uri(_deploymentResult.ApplicationBaseUri)).GetCookieWithName(".AspNet.Microsoft.AspNet.Identity.Application"));
            Assert.Null(_httpClientHandler.CookieContainer.GetCookies(new Uri(_deploymentResult.ApplicationBaseUri)).GetCookieWithName(".AspNet.Microsoft.AspNet.Identity.ExternalLogin"));
            _logger.LogInformation("Successfully signed in with user '{email}'", "microsoft@test.com");

            _logger.LogInformation("Verifying if the middleware events were fired");
            //Check for a non existing item
            response = await _httpClient.GetAsync(string.Format("Admin/StoreManager/GetAlbumIdFromName?albumName={0}", "123"));
            //This action requires admin permissions. If events are fired this permission is granted
            _logger.LogInformation(await response.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            _logger.LogInformation("Middleware events were fired successfully");
        }
    }
}