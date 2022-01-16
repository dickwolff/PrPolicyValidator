# Azure DevOps PR Policy Validator
This repository contains an Azure Function to validate wether GitVersion (and/or) CHANGELOG files have been edited.

### Use case

With my current project we use `GitVersion.yml` for source code versioning and update the `CHANGELOG.md` file with all the changes we do. This way we have a complete history of all changes we do to our software.

There were times when we forgot to change these files and thus we needed to add this as a PR policy. Manually checking is such a bore, so I decided to automate this.

### The inner working

The function is a HTTP triggered one that receives an Azure DevOps PR WebHook (to configure in Azure DevOps) as an input. After validating the request, the code will take a look at all the files changed in the PR and will check wether the `GitVersion.yml` is changed (and really updated). It will also do the same for the `CHANGELOG.md` file.

NOTE: The function expects the GitVersion to have a `next-version` property with 3 digits, e.g. `next-version: 0.1.0`.

### Options

Currently there are two options:
- By not adding parameters to the request URL the function will validate both the `GitVersion.yml` and `CHANGELOG.md` files.
- Adding `?validateChangelog=false` to the request URL will skip `CHANGELOG.md` validation. I added this option because it's something we do but not everyone will. 

### How to use

- Create an Azure Function App in Azure.
- Create a PAT in Azure Devops (on `https://dev.azure.com/{DEVOPS_ACCOUNT}/_usersSettings/tokens`). The PAT should have the policies Code (Read, write, & manage) and Pull Request Threads (Read & write).
- Add the `DEVOPS_ACCOUNT`, `DEVOPS_PROJECT` and `DEVOPS_PAT` settings to the Function App Configuration (environment variables).
- Deploy the project from Visual Studio to the deployed Azure Function App.
- Add the PR Validator to your Project (on `https://dev.azure.com/{DEVOPS_ACCOUNT}/{DEVOPS_PROJECT}/_settings/serviceHooks`).
  - Click the `+` icon to add a new Service Hook, scroll down and choose `Web Hook`.
  - Select `Pull request created` as the event trigger.
  - Add repository/branch/member/reviewer filters as prefered, or leave default to enable it for every repository.
  - On the URL, enter the Azure Function App URL, e.g. `https://myfunctionapp.azurewebsites.net/api/GitVersionChangeLogChecker` and click finish to add it.
  - Follow the steps above again for the `Pull request updated` event trigger.
- Go to the repository where you want the PR Policy validator to run. 
  - Navigate by Repository > Branches.
  - Click on the 3 dots on the right side of the branch you want to validate (e.g. main/master) and choose `Branch Policies`.
  - Scroll to `Status Checks` and click the `+` to add a new policy.
  - Select the policy from the drop down, and select the requirement as prefered.
    - Don't see the PR validator? Make sure the build has run at least once since you've added the policy in the previous steps. Otherwise it won't show up.
