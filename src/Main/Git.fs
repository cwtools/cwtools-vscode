module Main.Git
open LibGit2Sharp
open System.Text
open CWTools.Common
open System.IO
open System.Linq
open CWTools.Utilities.Utils

let rec initOrUpdateRules repoPath gameCacheDir stable first =
    if Directory.Exists gameCacheDir then () else Directory.CreateDirectory gameCacheDir |> ignore
    try
        let isRepo = Repository.IsValid gameCacheDir
        if isRepo then () else Repository.Clone(repoPath, gameCacheDir) |> ignore
        let git = new Repository(gameCacheDir)
        let remote = git.Network.Remotes.["origin"]
        let refSpecs = remote.FetchRefSpecs.Select((fun x -> x.Specification))
        Commands.Fetch(git, remote.Name, refSpecs, null, "")
        let currentHash = git.Head.Tip.Sha
        logInfo (sprintf "cwtools current rules version: %A" currentHash)
        match stable with
        |true ->
            let describeOptions = DescribeOptions()
            describeOptions.Strategy <- DescribeStrategy.Tags
            describeOptions.MinimumCommitIdAbbreviatedSize <- 0
            let tag = git.Describe(git.Branches.["origin/master"].Tip, describeOptions)
            let checkoutOptions = CheckoutOptions()
            checkoutOptions.CheckoutModifiers <- CheckoutModifiers.Force
            Commands.Checkout(git, tag, checkoutOptions) |> ignore
        |false ->
            git.Reset(ResetMode.Hard, git.Branches.["origin/master"].Tip)
        let newHash = git.Head.Tip.Sha
        logInfo (sprintf "cwtools new rules version: %A" newHash)
        (newHash <> currentHash) || not isRepo, Some git.Head.Tip.Committer.When
    with
    | ex ->
        logError (sprintf "cwtools git error, recovering, error: %A" ex)
        use git = new Repository(gameCacheDir)
        git.Reset(ResetMode.Hard, git.Branches.["origin/master"].Tip) |> ignore
        if first then initOrUpdateRules repoPath gameCacheDir stable false else (false, None)


 // var initOrUpdateRules = function(folder : string, repoPath : string, logger : vs.OutputChannel, first? : boolean) {
 //  const gameCacheDir = isDevDir ? context.storagePath + '/.cwtools/' + folder : context.extensionPath + '/.cwtools/' + folder
 //  var rulesVersion = "embedded"
 //  if (rulesChannel != "none") {
 //   !isDevDir || fs.existsSync(context.storagePath) || fs.mkdirSync(context.storagePath)
 //   fs.existsSync(cacheDir) || fs.mkdirSync(cacheDir)
 //   fs.existsSync(gameCacheDir) || fs.mkdirSync(gameCacheDir)
 //   const git = simplegit(gameCacheDir)
 //   let ret = git.checkIsRepo()
 //    .then(isRepo => !isRepo && git.clone(repoPath, gameCacheDir))
 //    .then(() => git.fetch())
 //    .then(() => git.log())
 //    .then((log) => { logger.appendLine("cwtools current rules version: " + log.latest.hash); return log.latest.hash })
 //    .then((prevHash : string) => { return Promise.all([prevHash, git.checkout("master")]) })
 //    //@ts-ignore
 //    .then(function ([prevHash, _]) { return Promise.all([prevHash, rulesChannel == "latest" ? git.reset(["--hard", "origin/master"]) : git.checkoutLatestTag()])} )
 //    .then(function ([prevHash, _]) { return Promise.all([prevHash, git.log()]) })
 //    .then(function ([prevHash, log]) { return log.latest.hash == prevHash ? undefined : log.latest.date })
 //    .catch(() => { logger.appendLine("cwtools git error, recovering"); git.reset(["--hard", "origin/master"]); first && initOrUpdateRules(folder, repoPath, logger, false) })
 //   return ret;
 //   }
 //  else {
 //   return Promise.resolve()
 //  }
 // }
