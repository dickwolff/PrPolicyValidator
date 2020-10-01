using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace PrPolicy
{
    /// <summary>
    /// PR Policy Azure Function.
    /// </summary>
    public class PrFunction
    {
        // Azure DevOps configuration.
        private readonly string DEVOPS_ACCOUNT = Environment.GetEnvironmentVariable("DEVOPS_ACCOUNT");
        private readonly string DEVOPS_PROJECT = Environment.GetEnvironmentVariable("DEVOPS_PROJECT");
        private readonly string DEVOPS_PAT = Environment.GetEnvironmentVariable("DEVOPS_PAT");

        /// <summary>
        /// HTTP Trigger to be called by Azure DevOps Web Hook.
        /// </summary>
        /// <param name="req">The request (received from Azure DevOps).</param>
        /// <param name="log">Logger instance.</param>
        /// <returns>HTTP result code stating success or failure.</returns>
        /// <response code="200">Validation was executed without errors.</response>
        /// <response code="400">An error occured while executing valitation. View response body for error.</response>
        /// <response code="404">The request did not contain an Azure DevOps PR.</response>
        [FunctionName("GitVersionChangeLogChecker")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)]
            HttpRequest req, ILogger log)
        {
            try
            {
                log.LogInformation("Service Hook Received.");

                // Validate if all settings are correct.
                var validation = ValidateSettings();
                if (validation != null)
                {
                    return validation;
                }

                // Get request body.
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                log.LogInformation($"Data Received: {requestBody}");

                // Get the pull request object from the service hooks payload.
                dynamic jObject = JsonConvert.DeserializeObject(requestBody);

                // Validate request. If null, then it's not an Azure DevOps PR request.
                if (jObject == null)
                {
                    return new NotFoundResult();
                }

                // Get the pull request id.
                int pullRequestId;
                if (!int.TryParse(jObject.resource.pullRequestId.ToString(), out pullRequestId))
                {
                    log.LogInformation("Failed to parse the pull request id from the service hooks payload.");
                }

                // Notify hook received.
                log.LogInformation($"Service Hook Received for PR: {pullRequestId} {jObject.resource.title}");

                // Read repository name.
                var repositoryName = (string)jObject.resource.repository.name;

                // Determine if changelog is optional.
                var clOptional = req.Query["validateChangelog"] == "false";

                // Validation container.
                // First bool = GitVersion edited.
                // Second bool = ChangeLog edited, value is default true when ChangeLog validation is optional.
                var prIsValid = new Tuple<bool, bool>(false, clOptional);

                // Go get all commits and convert to list.
                dynamic jCommits = await GetCommitsAsync(repositoryName, pullRequestId);
                var commits = jCommits.value.ToObject<List<dynamic>>();

                // Loop through commits and check the file changes.
                for (var idx = 0; idx < commits.Count; idx++)
                {
                    var commitId = (string)commits[idx].commitId;

                    // Get all changes for current commit.
                    var jChanges = await GetCommitAsync(repositoryName, commitId);
                    var changes = jChanges.changes.ToObject<List<dynamic>>();

                    for (var idy = 0; idy < changes.Count; idy++)
                    {
                        var change = changes[idy];

                        // Get name of changed file.
                        var path = (string)change.item.path;
                        var fileSplit = path.Split('/');
                        var fileName = fileSplit[fileSplit.Length - 1];

                        // Check GitVersion file.
                        if (fileName.ToLowerInvariant().Contains("gitversion.yml"))
                        {
                            // @todo: content validation

                            prIsValid = new Tuple<bool, bool>(true, prIsValid.Item2);
                        }

                        // Check Changelog file.
                        if (fileName.ToLowerInvariant().Contains("changelog.md"))
                        {
                            prIsValid = new Tuple<bool, bool>(prIsValid.Item1, true);
                        }
                    }
                }

                // Update the status on the PR.
                await PostStatusOnPullRequestAsync(repositoryName, pullRequestId, ComputeStatus(prIsValid));

                // Finally, OK.
                return new OkResult();
            }
            catch (Exception ex)
            {
                log.LogInformation(ex.ToString());
                return new BadRequestObjectResult(ex.Message);
            }
        }

        private IActionResult ValidateSettings()
        {
            if (string.IsNullOrEmpty(DEVOPS_ACCOUNT))
            {
                return new BadRequestObjectResult("Azure DevOps Account not configured!");
            }

            if (string.IsNullOrEmpty(DEVOPS_PROJECT))
            {
                return new BadRequestObjectResult("Azure DevOps Project not configured!");
            }

            if (string.IsNullOrEmpty(DEVOPS_PAT))
            {
                return new BadRequestObjectResult("Azure DevOps PAT not configured!");
            }

            return null;
        }

        private async Task<dynamic> GetCommitsAsync(string repositoryName, int pullRequestId)
        {
            var prUrl = string.Format(
                @"https://dev.azure.com/{0}/{1}/_apis/git/repositories/{2}/pullRequests/{3}/commits?api-version=5.1",
                DEVOPS_ACCOUNT,
                DEVOPS_PROJECT,
                repositoryName,
                pullRequestId);

            using (HttpClient client = new HttpClient())
            {
                AddAzureHeaders(client);

                var result = await client.GetAsync(prUrl).ConfigureAwait(false);
                var data = await result.Content.ReadAsStringAsync().ConfigureAwait(false);

                // Return the result as an usable object.
                return JsonConvert.DeserializeObject(data);
            }
        }

        private async Task<dynamic> GetCommitAsync(string repositoryName, string commitId)
        {
            string prUrl = string.Format(
               @"https://dev.azure.com/{0}/{1}/_apis/git/repositories/{2}/commits/{3}/changes?api-version=5.0",
               DEVOPS_ACCOUNT,
               DEVOPS_PROJECT,
               repositoryName,
               commitId);

            using (HttpClient client = new HttpClient())
            {
                AddAzureHeaders(client);

                var result = await client.GetAsync(prUrl);
                var data = await result.Content.ReadAsStringAsync();

                // Return the result as an usable object.
                return JsonConvert.DeserializeObject(data);
            }
        }

        private void AddAzureHeaders(HttpClient client)
        {
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(
                    Encoding.ASCII.GetBytes(
                    string.Format("{0}:{1}", "", DEVOPS_PAT))));
        }

        private async Task PostStatusOnPullRequestAsync(string repositoryName, int pullRequestId, string status)
        {
            string statusUrl = string.Format(
                @"https://dev.azure.com/{0}/{1}/_apis/git/repositories/{2}/pullrequests/{3}/statuses?api-version=4.1-preview.1",
                DEVOPS_ACCOUNT,
                DEVOPS_PROJECT,
                repositoryName,
                pullRequestId);

            using (var client = new HttpClient())
            {
                AddAzureHeaders(client);

                await client.PostAsync(statusUrl, new StringContent(status, Encoding.UTF8, "application/json"));
            }
        }

        private string ComputeStatus(Tuple<bool, bool> prStatus)
        {
            // Default OK.
            var state = "succeeded";
            var description = string.Empty;

            // If item1 is false, GitVersion file was not changed.
            if (!prStatus.Item1)
            {
                description = "GitVersion";
            }

            // If item2 is false, CHANGELOG file was not changed.
            if (!prStatus.Item2)
            {
                // If description has a value, add "en" to indicate both files have not been changed.
                if (!string.IsNullOrEmpty(description))
                {
                    description += " en ";
                }

                description += "CHANGELOG";
            }

            // If by this point description was empty, both files haven been changed.
            if (string.IsNullOrEmpty(description))
            {
                description = "Alle Git bestanden bijgewerkt.";
            }
            else
            {
                // At least one file has not been changed. Set status to fail and complete description text.
                state = "failed";
                description += " niet bijgewerkt.";
            }

            // Return object to send to Azure DevOps as string.
            return JsonConvert.SerializeObject(
                new
                {
                    State = state,
                    Description = description,
                    Context = new
                    {
                        Name = "Validatie Git Bestanden",
                        Genre = "Team Newton"
                    }
                });
        }
    }
}