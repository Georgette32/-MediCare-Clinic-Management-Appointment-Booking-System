using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace MediCare.Tests.Integration;

/// <summary>
/// One browser-like session against the in-process host: it keeps its cookies, never follows redirects, and carries the
/// antiforgery token from the page it is about to post from.
/// </summary>
public sealed partial class PageSession
{
    private const string TokenField = "__RequestVerificationToken";

    private PageSession(HttpClient http) => Http = http;

    public HttpClient Http { get; }

    /// <summary>Anonymous session (no cookies yet).</summary>
    public static PageSession Anonymous(MediCareWebApplicationFactory factory) => new(factory.CreateSessionClient());

    /// <summary>Signs in through the real login form and fails the test unless the server redirects to the home page.</summary>
    public static async Task<PageSession> SignInAsync(MediCareWebApplicationFactory factory, string userName, string password)
    {
        var session = Anonymous(factory);
        var response = await session.PostFormAsync(
            "/Account/Login",
            "/Account/Login",
            [new("UserName", userName), new("Password", password), new("RememberMe", "false"), new("ReturnUrl", "")]);
        if (response.StatusCode != HttpStatusCode.Redirect || LocationPath(response) != "/")
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Sign-in for '{userName}' failed: {(int)response.StatusCode} {LocationPath(response)} {Summarize(body)}");
        }
        return session;
    }

    public Task<HttpResponseMessage> GetAsync(string path) => Http.GetAsync(path);

    /// <summary>GET that must return 200 and returns the HTML.</summary>
    public async Task<string> GetOkHtmlAsync(string path)
    {
        var response = await Http.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"GET {path} returned {(int)response.StatusCode} (location {LocationPath(response)}): {Summarize(body)}");
        return body;
    }

    /// <summary>Gets a page and returns the antiforgery token it renders (the token is valid for any form on the same session).</summary>
    public async Task<string> AntiForgeryTokenFromAsync(string pagePath)
    {
        var html = await GetOkHtmlAsync(pagePath);
        return ExtractAntiForgeryToken(html);
    }

    /// <summary>Posts an urlencoded form. The token is read from <paramref name="tokenPagePath"/> first.</summary>
    public async Task<HttpResponseMessage> PostFormAsync(string formPath, string tokenPagePath, IEnumerable<KeyValuePair<string, string>> fields)
    {
        var token = await AntiForgeryTokenFromAsync(tokenPagePath);
        var values = new List<KeyValuePair<string, string>> { new(TokenField, token) };
        values.AddRange(fields);
        using var content = new FormUrlEncodedContent(values);
        return await Http.PostAsync(formPath, content);
    }

    /// <summary>Posts a multipart form (file uploads). The token is read from <paramref name="tokenPagePath"/> first.</summary>
    public async Task<HttpResponseMessage> PostMultipartAsync(
        string formPath,
        string tokenPagePath,
        IEnumerable<KeyValuePair<string, string>> fields,
        string fileField,
        string fileName,
        string contentType,
        byte[] fileBytes)
    {
        var token = await AntiForgeryTokenFromAsync(tokenPagePath);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(token, Encoding.UTF8), TokenField);
        foreach (var (name, value) in fields)
            content.Add(new StringContent(value, Encoding.UTF8), name);
        var file = new ByteArrayContent(fileBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, fileField, fileName);
        return await Http.PostAsync(formPath, content);
    }

    public static string ExtractAntiForgeryToken(string html)
    {
        var input = Regex.Match(html, "<input[^>]*name=\"__RequestVerificationToken\"[^>]*>", RegexOptions.IgnoreCase);
        if (!input.Success)
            throw new InvalidOperationException("The page does not contain an antiforgery token.");
        var value = Regex.Match(input.Value, "value=\"([^\"]*)\"", RegexOptions.IgnoreCase);
        if (!value.Success)
            throw new InvalidOperationException("The antiforgery token input has no value.");
        return WebUtility.HtmlDecode(value.Groups[1].Value);
    }

    /// <summary>The redirect target without the scheme and host, or an empty string when there is no Location header.</summary>
    public static string LocationPath(HttpResponseMessage response)
    {
        var location = response.Headers.Location;
        if (location is null)
            return string.Empty;
        return location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
    }

    private static string Summarize(string html)
    {
        var text = WhitespaceRegex().Replace(TagRegex().Replace(html, " "), " ").Trim();
        return text.Length > 300 ? text[..300] + "..." : text;
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
