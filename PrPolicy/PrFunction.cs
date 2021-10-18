using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PrPolicy
{
    /// <summary>
    /// PR Policy Azure Function.
    /// </summary>
    public class PrFunction
    {
        // Azure DevOps configuration (from settings.json).
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
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
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
                var repositoryId = (string)jObject.resource.repository.id;

                // Determine if changelog is optional.
                var clOptional = req.Query["validateChangelog"] == "false";

                // Validation container.
                // First bool = GitVersion edited.
                // Second bool = ChangeLog edited, value is default true when ChangeLog validation is optional.
                var prIsValid = new Tuple<bool, bool>(false, clOptional);

                // Go get all commits and convert to list.
                var commits = await GetCommitsAsync(repositoryId, pullRequestId);

                // Loop through commits and check the file changes.
                foreach (var commit in commits)
                {
                    // Get all changes for current commit.
                    var commitSet = await GetCommitAsync(repositoryId, commit.CommitId);

                    // If there are no changes, skip the commit.
                    if (commitSet.Changes == null)
                    {
                        continue;
                    }

                    foreach (var change in commit.Changes)
                    {
                        // Get name of changed file.
                        var fileSplit = change.Item.Path.Split('/');
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
                await PostStatusOnPullRequestAsync(repositoryId, pullRequestId, ComputeStatus(prIsValid));

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

        private async Task<List<GitCommitRef>> GetCommitsAsync(string repositoryId, int pullRequestId)
        {
            var connection = CreateConnection();
            using (var client = connection.GetClient<GitHttpClient>())
            {
                return await client.GetPullRequestCommitsAsync(repositoryId, pullRequestId);
            }
        }

        private async Task<GitCommitChanges> GetCommitAsync(string repositoryId, string commitId)
        {
            var connection = CreateConnection();
            using (var client = connection.GetClient<GitHttpClient>())
            {
                return await client.GetChangesAsync(commitId, repositoryId);
            }
        }

        private async Task PostStatusOnPullRequestAsync(string repositoryId, int pullRequestId, GitPullRequestStatus status)
        {
            var connection = CreateConnection();
            using (var client = connection.GetClient<GitHttpClient>())
            {
                await client.CreatePullRequestStatusAsync(status, repositoryId, pullRequestId);
            }
        }

        private VssConnection CreateConnection()
        {
            return new VssConnection(new Uri($"https://dev.azure.com/{DEVOPS_ACCOUNT}"), new VssBasicCredential(string.Empty, DEVOPS_PAT));
        }

        private GitPullRequestStatus ComputeStatus(Tuple<bool, bool> prStatus)
        {
            // Default OK.
            var state = GitStatusState.Succeeded;
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
                state = GitStatusState.Failed;
                description += " niet bijgewerkt.";
            }

            // Return object to send to Azure DevOps as string.
            return new GitPullRequestStatus
            {
                State = state,
                Description = description,
                Context = new GitStatusContext
                {
                    Name = "Validatie Git Bestanden",
                    Genre = "Team Newton"
                }
            };
        }
    }
}