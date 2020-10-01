# Azure DevOps PR Policy Validator
This repository contains an Azure Function to validate wether GitVersion (and/or) CHANGELOG files have been edited.

### Use case

With my current project we use `GitVersion.yml` for source code versioning and update the `CHANGELOG.md` file with all the changes we do. This way we have a complete history of all changes we do to our software.

There were times when we forgot to change these files and thus we needed to add this as a PR policy. Manually checking is such a bore, so I decided to automate this.

### The inner working

The function is a HTTP triggered one that receives an Azure DevOps PR WebHook (to configure in Azure DevOps) as an input. After validating the request, the code will take a look at al the files changed in the PR and will check wether the `GitVersion.yml` is changed (and really updated). It will also do the same for the `CHANGELOG.md` file.

### Options

Currently there are two options:
- By not adding parameters to the request URL the function will validate both the `GitVersion.yml` and `CHANGELOG.md` files.
- Adding `?validateChangelog=false` to the request URL will skip `CHANGELOG.md` validation. I added this option because it's something we do but not everyone will. 
