using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Spectre.Console;

namespace GateKeeperHub.Api.Demo
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Display application header
            AnsiConsole.Write(
                new Panel("[bold green]GateKeeper Hub API Demo[/]")
                    .Border(BoxBorder.Rounded)
                    .Header("[bold yellow]Welcome[/]", Justify.Center)
                    .Collapse()
                    .Padding(2, 1));

            while (true)
            {
                // Step 1: Input Server URL, Username, and Password
                AnsiConsole.Markup("[bold yellow]Login to Hub[/]\n");
                string serverUrl = null;

                // Step 1.1: Validate the Server URL
                while (true)
                {
                    serverUrl = AnsiConsole.Ask<string>("[bold]Enter the [green]Server URL[/]:[/]");

                    var serverInfo = await AnsiConsole.Status()
                        .StartAsync("Validating server URL...", async ctx =>
                        {
                            return await ValidateServerUrlAsync(serverUrl);
                        });

                    if (serverInfo == null || !serverInfo.success)
                    {
                        AnsiConsole.Markup("[bold red]Invalid server URL or failed validation. Please try again.[/]\n");
                        continue;
                    }

                    AnsiConsole.Markup($"[bold green]Connected to Hub version {serverInfo.serverVersion}.[/]\n");
                    break;
                }

                var username = AnsiConsole.Ask<string>("[bold]Enter your [green]Username[/]:[/]");
                var password = AnsiConsole.Prompt(
                    new TextPrompt<string>("[bold]Enter your [green]Password[/]:[/]")
                        .Secret());

                // Step 2: Authenticate and get JWT Token with loading spinner
                var (token, twoFAStatus) = await AnsiConsole.Status()
                    .StartAsync("Authenticating...", async ctx =>
                    {
                        return await GetJwtTokenAsync(serverUrl, username, password);
                    });

                if (token == null && twoFAStatus == null)
                {
                    AnsiConsole.Markup("[bold red]Authentication failed! Please try again.[/]\n");
                    continue;
                }

                // Step 2.1: Handle Two-Factor Authentication (2FA) if required
                if (twoFAStatus != null && twoFAStatus.status == 230)
                {
                    AnsiConsole.Markup("[bold yellow]Two-Factor Authentication required.[/]\n");

                    var twoFAType = string.Empty;
                    var code = string.Empty;
                    var types = new List<string>();

                    // Add available 2FA types
                    if (twoFAStatus.phoneEnabled)
                    {
                        types.Add("Phone");
                    }
                    if (twoFAStatus.authenticatorEnabled)
                    {
                        types.Add("Authenticator App");
                    }
                    types.Add("Backup Codes");

                    // Prompt user to select 2FA method
                    twoFAType = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("[bold]Select the 2FA method[/]:")
                            .AddChoices(types));

                    while (true)
                    {
                        
                        if (twoFAType == "Phone")
                        {
                            
                            // Send 2FA code to phone and verify. 1 as argument here is for sending sms
                            await SendTwoFACodeAsync(serverUrl, token, 1, twoFAStatus.stamp);
                            code = AnsiConsole.Ask<string>("[bold]Enter the [green]code sent to your phone[/]:[/]");
                            var result = await VerifyTwoFACodeAsync(serverUrl, token, 1, code, twoFAStatus.stamp);
                            if (result.Success)
                            {
                                token = result.Jwt;
                                break; // Exit loop if verification is successful
                            }
                            else
                            {
                                AnsiConsole.Markup("[bold red]Invalid code. Please try again.[/]\n");
                            }
                        }
                        else if (twoFAType == "Authenticator App")
                        {
                            // Verify 2FA code from authenticator app
                            code = AnsiConsole.Ask<string>("[bold]Enter the [green]code from your authenticator app[/]:[/]");
                            var result = await VerifyTwoFACodeAsync(serverUrl, token, 3, code, twoFAStatus.stamp);
                            if (result.Success)
                            {
                                token = result.Jwt;
                                break; // Exit loop if verification is successful
                            }
                            else
                            {
                                AnsiConsole.Markup("[bold red]Invalid code. Please try again.[/]\n");
                            }
                        }
                        else if (twoFAType == "Backup Codes")
                        {
                            // Verify backup code
                            code = AnsiConsole.Ask<string>("[bold]Enter the [green]backup code[/]:[/]");
                            var result = await VerifyTwoFACodeAsync(serverUrl, token, 4, code, twoFAStatus.stamp);
                            if (result.Success)
                            {
                                token = result.Jwt;
                                break; // Exit loop if verification is successful
                            }
                            else
                            {
                                AnsiConsole.Markup("[bold red]Invalid code. Please try again.[/]\n");
                            }
                        }
                    }
                }

                // Step 3: Fetch list of users with loading spinner
                var users = await AnsiConsole.Status()
                    .StartAsync("Loading users...", async ctx =>
                    {
                        return await GetUsersAsync(serverUrl, token);
                    });

                if (users == null || users.Count == 0)
                {
                    AnsiConsole.Markup("[bold red]No users found![/]\n");
                    continue;
                }

                while (true)
                {
                    // Step 4: Select a user
                    var selectedUser = AnsiConsole.Prompt(
                        new SelectionPrompt<User>()
                            .Title($"[bold green]Select a user to view details:[/] (Total users: [yellow]{users.Count}[/])")
                            .PageSize(15)
                            .HighlightStyle(new Style(foreground: Color.Cyan1, decoration: Decoration.Bold))
                            .MoreChoicesText("[grey](Move up and down to reveal more users...)[/]")
                            .AddChoices(users)
                    );

                    // Step 5: Fetch and display selected user details with a fancy panel
                    var userDetails = await GetUserDetailsAsync(serverUrl, token, selectedUser.Id);
                    if (userDetails != null)
                    {
                        var userPanel = new Panel(
                            $"[bold yellow]Name[/]: {userDetails.Name}\n" +
                            $"[blue]ID[/]: {userDetails.Id}\n" +
                            $"[blue]Email[/]: {userDetails.Email}\n" +
                            $"[blue]Phone[/]: {userDetails.PhoneNumber}\n" +
                            $"[blue]Employee ID[/]: {userDetails.EmployeeId}")
                            .Border(BoxBorder.Rounded)
                            .Header($"Details for {userDetails.Name}", Justify.Center)
                            .Padding(2, 1);

                        AnsiConsole.Write(userPanel);
                    }

                    // Ask if the user wants to select another user
                    if (!AnsiConsole.Confirm("[bold]Do you want to select another user?[/]"))
                    {
                        break;
                    }
                }
            }
        }

        // Method to validate the server URL
        static async Task<ServerInfo> ValidateServerUrlAsync(string serverUrl)
        {
            var client = new HttpClient();
            try
            {
                var response = await client.GetAsync($"{serverUrl}/hub/api/v4/server/version");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<ServerInfo>(responseJson);
                }
            }
            catch
            {
                // Handle exceptions (e.g., invalid URL or network error)
            }
            return null;
        }

        // Method to get JWT Token
        static async Task<(string, TwoFAStatus)> GetJwtTokenAsync(string serverUrl, string username, string password)
        {
            var client = new HttpClient();
            var requestBody = new
            {
                Username = username,
                Password = password
            };
            var jsonBody = JsonConvert.SerializeObject(requestBody);
            var response = await client.PostAsync($"{serverUrl}/token",
                new StringContent(jsonBody, Encoding.UTF8, "application/json"));

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                var jsonResponse = JsonConvert.DeserializeObject<AccessToken>(responseJson);
                jsonResponse.twoFA.status = (int)response.StatusCode;
                return (jsonResponse.token?.jwt, jsonResponse.twoFA);
            }

            return (null, null);
        }

        // Method to send 2FA code to the phone

        
        static async Task<string> SendTwoFACodeAsync(string serverUrl, string token, int type, string stamp)
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var requestBody = new
            {
                stamp = stamp,
                type = type
            };
            var jsonBody = JsonConvert.SerializeObject(requestBody);
            var response = await client.PostAsync($"{serverUrl}/send2fa",
                new StringContent(jsonBody, Encoding.UTF8, "application/json"));

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }
            else
            {
                AnsiConsole.Markup("[bold red]Failed to send 2FA code. Please try again.[/]\n");
                return null;
            }
        }

        // Method to verify 2FA code

        //type: 1 (SMS), 3 (Authenticator), 4 (Backup Codes)
        static async Task<(bool Success, string Jwt)> VerifyTwoFACodeAsync(string serverUrl, string token, int type, string code, string stamp)
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var requestBody = new
            {
                stamp = stamp,
                type = type,
                code = code
            };
            var jsonBody = JsonConvert.SerializeObject(requestBody);
            var response = await client.PostAsync($"{serverUrl}/2fatoken",
                new StringContent(jsonBody, Encoding.UTF8, "application/json"));

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                var jsonResponse = JsonConvert.DeserializeObject<AccessToken>(responseJson);
                return (true, jsonResponse.token?.jwt);
            }
            else
            {
                return (false, null);
            }
        }

        // Method to get the list of users
        static async Task<List<User>> GetUsersAsync(string serverUrl, string token)
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync($"{serverUrl}/hub/api/v4/GateKeeperUsers");
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<User>>(responseJson);
            }
            return new List<User>();
        }

        // Method to get details of a specific user
        static async Task<User> GetUserDetailsAsync(string serverUrl, string token, string userId)
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync($"{serverUrl}//hub/api/v4/GateKeeperUsers/{userId}");
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<User>(responseJson);
            }
            return null;
        }

        // Model classes for JSON deserialization
        public class AccessToken
        {
            public Token token { get; set; }
            public TwoFAStatus twoFA { get; set; }
        }

        public class Token
        {
            public string jwt { get; set; }
        }

        public class TwoFAStatus
        {
            public int status { get; set; }
            public string stamp { get; set; }
            public bool phoneEnabled { get; set; }
            public bool authenticatorEnabled { get; set; }
        }

        public class User
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Email { get; set; }
            public string PhoneNumber { get; set; }
            public string EmployeeId { get; set; }
            public override string ToString()
            {
                return $"{Name} ({Email})";
            }
        }

        public class ServerInfo
        {
            public bool success { get; set; }
            public string serverVersion { get; set; }
        }
    }
}
