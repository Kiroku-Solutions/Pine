using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Pine.Tests.Unit.Build;

/// <summary>
/// The release artefact's signing path (TD-108, <c>62-Post-Audit-Plan</c> item 2.2).
/// </summary>
/// <remarks>
/// <para>
/// WHAT THESE ARE AND ARE NOT. Every assertion below is a text scan over a script or a workflow.
/// That is a weak instrument and it is stated plainly here rather than discovered by a reader: a
/// scan can be evaded by string concatenation, by moving the work into a file nothing here reads,
/// or by any construction that spells the same behaviour differently. It raises the cost of undoing
/// this item BY ACCIDENT, which is how it was broken in the first place — the inline signing step in
/// <c>package.sh</c> was never executed by anything, so no test had to be defeated for it to be
/// wrong for twenty-four iterations.
/// </para>
/// <para>
/// THE FIRST VERSION OF EVERY TEST IN THIS FILE WAS DEFEATED BY AN ORDINARY EDIT TO THE LINE IT
/// GUARDS, and the independent review of item 2.2 found all six at once: an export whose VALUE was
/// emptied, a rogue signing script whose invocation was spelled without quotes, a
/// <c>--require-signature</c> moved to the other branch of the same step, the two artefact names
/// SWAPPED between the case arms, an escape hatch set in <c>package.sh</c> instead of the workflow,
/// and a <c>|| true</c> appended to the self-test. None of those is "string concatenation"; they
/// are the edits a person would actually make. Each test below now asserts the RELATIONSHIP the
/// arrangement depends on rather than the presence of a word.
/// </para>
/// <para>
/// THE REAL EVIDENCE IS ELSEWHERE, AND ON PURPOSE. <c>packaging/selftest-signing.sh</c> runs the
/// signing path end to end against a throwaway self-signed certificate and falsifies each refusal,
/// and the CI <c>test</c> job runs it on every pull request. These tests exist to guard the parts of
/// the arrangement that live in YAML and cannot be executed from xUnit.
/// </para>
/// </remarks>
public partial class BuildHygieneTests
{
    /// <summary>
    /// The scripts permitted to invoke <c>signtool</c>. Anything else signing binaries would be
    /// signing them without <c>packaging/sign.sh</c>'s verification pass behind it.
    /// </summary>
    private static readonly string[] ScriptsAllowedToRunSigntool =
    {
        "sign.sh",
        "find-signtool.sh",
        "selftest-signing.sh",
    };

    /// <summary>
    /// Environment variables that turn a signing gate into a formality. Only the self-test may set
    /// them.
    /// </summary>
    private static readonly string[] SigningEscapeHatches =
    {
        "PINE_SIGNING_ACCEPT_UNTRUSTED_ROOT",
        "PINE_SIGNING_ALLOW_UNTIMESTAMPED",
    };

    /// <summary>
    /// Anything that looks like a <c>signtool … sign</c> invocation, however the path to it is
    /// spelled.
    /// </summary>
    /// <remarks>
    /// The first version matched the two literal spellings this repository happened to use, and the
    /// independent review defeated it with <c>$PINE_SIGNTOOL sign …</c> — the same command without
    /// the quotes. Matching the variable's NAME followed by the verb covers every spelling of the
    /// path that still reads as a signing call.
    /// </remarks>
    private static readonly Regex SigntoolInvocation = new(
        @"(?i)signtool[""'}\]]*\s+sign(\s|$)",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The directories whose shell scripts are scanned for a stray <c>signtool</c> call. Held in a
    /// field for CA1861, like every other constant array in this class.
    /// </summary>
    private static readonly string[] DirectoriesHoldingShellScripts = { "packaging", "scripts" };

    /// <summary>
    /// The only job that may run the signing self-test. A field for CA1861.
    /// </summary>
    private static readonly string[] JobsThatRunTheSigningSelfTest = { "test" };

    private static string[] SignScriptLines() =>
        File.ReadAllLines(Path.Combine(RepositoryRoot(), "packaging", "sign.sh"));

    private static string[] WorkflowLines() =>
        File.ReadAllLines(Path.Combine(RepositoryRoot(), ".github", "workflows", "dotnet-ci.yml"));

    /// <summary>
    /// The one-line defect this item found: under Git Bash, MSYS rewrites <c>/fd</c> into a path
    /// before <c>signtool</c> sees it, and <c>signtool</c> then reports the flag as missing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured on 2026-09-12: the pre-fix command form fails with "No file digest algorithm
    /// specified" while <c>/fd SHA256</c> is on the command line, and the identical command succeeds
    /// with <c>MSYS2_ARG_CONV_EXCL</c> exported. That measurement is kept executable as case I of
    /// <c>packaging/selftest-signing.sh</c>.
    /// </para>
    /// <para>
    /// THE VALUE IS ASSERTED, NOT ONLY THE NAME. <c>export MSYS2_ARG_CONV_EXCL=''</c> exports
    /// nothing while satisfying every weaker form of this test — the variable is set, it is before
    /// the call, and signtool is handed a rewritten <c>/fd</c> exactly as before.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSigningStepTurnsOffMsysPathConversionBeforeItCallsSigntool()
    {
        // LINES, AND ONLY UNCOMMENTED ONES. An earlier spelling searched the whole file for the
        // string and PASSED against a mutation that deleted the export outright — because the long
        // comment above it explaining why the export matters still contained the word. A scan that
        // matches the comment instead of the code guards the explanation.
        var lines = SignScriptLines();

        var guard = Array.FindIndex(
            lines,
            line => IsUncommented(line, "export MSYS2_ARG_CONV_EXCL"));

        var call = Array.FindIndex(lines, line => SigntoolInvocation.IsMatch(line) && !line.TrimStart().StartsWith('#'));

        guard.Should().BeGreaterThan(
            -1,
            "without MSYS2_ARG_CONV_EXCL, Git Bash rewrites /fd into a path and signtool rejects "
            + "the flag it was given. This is the defect that made the old signing seam unable to "
            + "sign anything at all");

        call.Should().BeGreaterThan(-1, "packaging/sign.sh must actually invoke signtool");

        guard.Should().BeLessThan(
            call,
            "MSYS2_ARG_CONV_EXCL has to be exported before signtool is called; set afterwards it "
            + "guards nothing and only looks like it does");

        lines[guard].Should().MatchRegex(
            @"export MSYS2_ARG_CONV_EXCL='\*'",
            "the value has to be the wildcard. An empty one is set, is before the call, and turns "
            + "off nothing at all — signtool receives a rewritten /fd exactly as it did before this "
            + "item was worked");
    }

    /// <summary>
    /// Signing happens in one place, so that verification cannot be bypassed by signing somewhere
    /// else.
    /// </summary>
    /// <remarks>
    /// <c>packaging/sign.sh</c> signs and then proves it signed: every loadable file carries a
    /// signature, every signature matches its bytes, every certificate chains to a trusted root. A
    /// second caller of <c>signtool</c> would produce signatures nothing checked — which is what
    /// <c>package.sh</c> used to do, and the reason its <c>"signed": true</c> was a claim rather
    /// than a measurement. <c>scripts/</c> is scanned as well as <c>packaging/</c>: the review put
    /// its rogue signer in <c>packaging/</c>, but nothing stopped it living one directory away.
    /// </remarks>
    [Fact]
    public void OnlyTheSigningScriptInvokesSigntool()
    {
        var root = RepositoryRoot();

        var searched = DirectoriesHoldingShellScripts
            .Select(directory => Path.Combine(root, directory))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.GetFiles(directory, "*.sh", SearchOption.AllDirectories))
            .ToArray();

        searched.Should().NotBeEmpty("there are shell scripts to scan");

        var offenders = searched
            .Where(path => !ScriptsAllowedToRunSigntool.Contains(Path.GetFileName(path), StringComparer.Ordinal))
            .Where(path => File
                .ReadAllLines(path)
                .Any(line => !line.TrimStart().StartsWith('#') && SigntoolInvocation.IsMatch(line)))
            .Select(Path.GetFileName)
            .ToArray();

        offenders.Should().BeEmpty(
            "signing must go through packaging/sign.sh, which verifies what it signed; a second "
            + "caller would emit signatures that nothing ever checks");
    }

    /// <summary>
    /// A configured certificate and <c>--require-signature</c> are one decision, not two.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TD-108's Fix column asks for <c>--require-signature</c> "so an unsigned build can never be
    /// published by accident". The accident it is guarding is not forgetting to configure a
    /// certificate — that is loud. It is configuring one that is expired, wrongly encoded, or
    /// password-mismatched, and having the pipeline shrug and publish an unsigned build under the
    /// release name.
    /// </para>
    /// <para>
    /// SO THE FLAG IS PINNED TO THE RIGHT INVOCATION, not merely present somewhere in the step. Both
    /// branches live in one `run:` block, so "the step mentions --require-signature" was satisfied
    /// by moving the flag to the branch that has no certificate — where it would refuse every
    /// unsigned build and never guard a signed one.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCiPackageJobRequiresASignatureWheneverACertificateIsConfigured()
    {
        var job = WorkflowJobs().Single(entry => entry.Key == "package");
        var steps = WorkflowSteps(job.Value);

        var signing = steps
            .Where(step => step.Lines.Any(line => IsUncommented(line, "PINE_SIGNING_CERT=")))
            .ToArray();

        signing.Should().HaveCount(
            1,
            "exactly one step may configure the signing certificate; two would mean two policies");

        var lines = signing[0].Lines;

        var certificate = lines.FindIndex(line => IsUncommented(line, "PINE_SIGNING_CERT="));

        var invocations = lines
            .Select((line, index) => (line, index))
            .Where(entry => IsUncommented(entry.line, "packaging/package.sh"))
            .ToArray();

        invocations.Should().HaveCount(
            2,
            "there are exactly two ways to build the artefact — with a certificate and without — and "
            + "a third invocation would be a third policy nothing here describes");

        var withFlag = invocations.Where(entry => entry.line.Contains("--require-signature", StringComparison.Ordinal)).ToArray();

        withFlag.Should().HaveCount(
            1,
            "exactly one of the two invocations demands a signature: the one that has a certificate");

        // BETWEEN THE CERTIFICATE AND THE `else`, which is the only way to say "inside that branch"
        // from a text scan. "After the certificate line" was the first spelling and it was wrong:
        // moving the flag down to the UNSIGNED branch still satisfies it, and that arrangement
        // refuses every unsigned build while guarding no signed one. Both branches live in one
        // `run:` block, so the `else` is the boundary that matters.
        var branchEnd = lines.FindIndex(certificate, line => line.Trim() == "else");

        branchEnd.Should().BeGreaterThan(
            certificate,
            "the certificate branch must have an `else`: the unsigned build is the other outcome, "
            + "and a step with only one of them cannot be honest about which one it produced");

        withFlag[0].index.Should().BeInRange(
            certificate,
            branchEnd,
            "the invocation carrying --require-signature must be the one INSIDE the branch that "
            + "configured a certificate. Moved past the `else` it refuses every unsigned build and "
            + "guards no signed one, while every weaker form of this test still passes");

        invocations
            .Single(entry => !entry.line.Contains("--require-signature", StringComparison.Ordinal))
            .index.Should().BeGreaterThan(
                branchEnd,
                "the invocation WITHOUT the flag is the unsigned branch's, and it belongs after the "
                + "`else`");

        var text = string.Join('\n', lines);

        text.Should().Contain(
            "signing:signed",
            "the signed branch must leave a receipt, or nothing can prove which branch ran");

        text.Should().Contain(
            "signing:unsigned",
            "the unsigned branch must leave its own receipt; a single receipt for both would make "
            + "the two indistinguishable afterwards");
    }

    /// <summary>
    /// An unsigned artefact is not uploaded under the name a signed one would have.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the half of TD-108 that can be closed without a certificate. The row's Impact is "a
    /// user cannot distinguish the project's artefact from one a third party built and modified".
    /// Until a certificate exists they cannot — so the artefact says so in its own name.
    /// </para>
    /// <para>
    /// THE MAPPING IS ASSERTED, NOT THE VOCABULARY. Swapping the two case arms leaves both names in
    /// the file, leaves the output expression untouched, and uploads every unsigned build as
    /// <c>pine-win-x64</c> — the precise harm this test is named after. The independent review did
    /// exactly that and all six guards stayed green.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnUnsignedArtefactIsNotUploadedUnderTheReleaseName()
    {
        var job = WorkflowJobs().Single(entry => entry.Key == "package");
        var lines = job.Value;

        var upload = WorkflowSteps(lines)
            .Single(step => step.Lines.Any(line => IsUncommented(line, "upload-artifact")));

        string.Join('\n', upload.Lines).Should().Contain(
            "steps.artefact.outputs.artifact_name",
            "the uploaded artefact's name must be computed from what the manifest says about "
            + "signing, not written as a literal that is right in only one of the two cases");

        var signedArm = lines.SingleOrDefault(line => IsUncommented(line, "true)") && line.Contains("name=", StringComparison.Ordinal));
        var unsignedArm = lines.SingleOrDefault(line => IsUncommented(line, "false)") && line.Contains("name=", StringComparison.Ordinal));

        signedArm.Should().NotBeNull("the manifest's `signed: true` case must name the artefact");
        unsignedArm.Should().NotBeNull("the manifest's `signed: false` case must name the artefact");

        signedArm!.Should().NotContain(
            "UNSIGNED",
            "a SIGNED artefact must not be uploaded under the unsigned name — the two arms have "
            + "been swapped");

        unsignedArm!.Should().Contain(
            "UNSIGNED",
            "an UNSIGNED artefact must not be uploaded under the release name. A person who "
            + "downloads `pine-win-x64` and meets SmartScreen has been misled by this pipeline, "
            + "which is TD-108's whole impact");
    }

    /// <summary>
    /// Nothing in the release path sets the two escape hatches the self-test needs.
    /// </summary>
    /// <remarks>
    /// <c>PINE_SIGNING_ACCEPT_UNTRUSTED_ROOT</c> and <c>PINE_SIGNING_ALLOW_UNTIMESTAMPED</c> exist so
    /// that <c>packaging/selftest-signing.sh</c> can exercise the real path with a throwaway
    /// certificate and no network. Anywhere else either one turns a gate into a formality: a
    /// signature whose root nobody trusts protects nobody, and an untimestamped one expires with the
    /// certificate. The first version of this test read only the workflow, and the independent
    /// review set the hatch in <c>package.sh</c> instead — one file away, same effect, test green.
    /// </remarks>
    [Fact]
    public void NothingInTheReleasePathWeakensTheSigningGates()
    {
        var root = RepositoryRoot();

        var files = new[] { Path.Combine(root, ".github", "workflows", "dotnet-ci.yml") }
            .Concat(Directory
                .GetFiles(Path.Combine(root, "packaging"), "*.sh", SearchOption.AllDirectories)
                .Where(path => !string.Equals(Path.GetFileName(path), "selftest-signing.sh", StringComparison.Ordinal)))
            .ToArray();

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);

            foreach (var hatch in SigningEscapeHatches)
            {
                // AN ASSIGNMENT, NOT A MENTION. packaging/sign.sh prints both variable names in its
                // own warnings — it has to, because the message a person needs when a gate refuses
                // is the name of the switch that would change the answer. A scan for the bare name
                // would fail on exactly the file that implements the gate correctly, which is the
                // same trap the TD-140 guard fell into (`62-Post-Audit-Plan` §5.1).
                var assignment = new Regex(
                    @"(?:^|[\s;])(?:export\s+)?" + Regex.Escape(hatch) + "=",
                    RegexOptions.None,
                    TimeSpan.FromSeconds(5));

                lines.Should().NotContain(
                    line => !line.TrimStart().StartsWith('#')
                        && !line.Contains("echo", StringComparison.Ordinal)
                        && assignment.IsMatch(line),
                    "'{0}' sets {1}, which only packaging/selftest-signing.sh may do. A signature "
                    + "that chains to a root nobody has, or that stops validating the day the "
                    + "certificate expires, is not a signature a user can act on",
                    Path.GetFileName(file),
                    hatch);
            }
        }
    }

    /// <summary>
    /// The signing self-test runs on every pull request, and a failure fails the build.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TD-108's real lesson is not "no certificate". It is that a seam nothing executes rots without
    /// anything going red: the old signing step could not have worked, and no test, no build and no
    /// reviewer would have found that out before the first real release. A self-test that lives in
    /// the repository and is never run would repeat the mistake one level up.
    /// </para>
    /// <para>
    /// IT MUST BE IN THE <c>test</c> JOB. It started in <c>package</c>, which carries
    /// <c>if: github.event_name != 'pull_request'</c> — so it ran only after a merge, while the step
    /// itself carried a comment claiming it ran on every push and this test asserted the step had no
    /// condition. A gate on the way in is worth more than a gate after the fact.
    /// </para>
    /// <para>
    /// AND A FAILURE MUST FAIL. <c>|| true</c> leaves the invocation in place, leaves the receipt
    /// recorded, and turns the whole thing into a log message.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSigningSelfTestRunsOnEveryPullRequestAndItsFailureIsFatal()
    {
        var jobs = WorkflowJobs();

        var running = jobs
            .Where(job => job.Value.Any(line => IsUncommented(line, "selftest-signing.sh")))
            .Select(job => job.Key)
            .ToArray();

        running.Should().BeEquivalentTo(
            JobsThatRunTheSigningSelfTest,
            "the signing self-test belongs to the job that runs on pull requests. In `package` it "
            + "would be skipped by that job's `if:` on every pull request, so a change that broke "
            + "signing would go green in review and only fail after it had been merged");

        var steps = WorkflowSteps(jobs["test"]);

        var selfTest = steps
            .Where(step => step.Lines.Any(line => IsUncommented(line, "selftest-signing.sh")))
            .ToArray();

        selfTest.Should().HaveCount(1, "one step runs it");

        var invocation = selfTest[0].Lines.Single(line => IsUncommented(line, "selftest-signing.sh"));

        invocation.Should().NotContain(
            "||",
            "a self-test whose failure is swallowed is a log message, not a gate");

        selfTest[0].Lines.Should().Contain(
            line => IsUncommented(line, "ci-receipts.sh record signing-selftest"),
            "the self-test must leave a receipt, or a step that silently stopped running it would "
            + "still report green");

        // The step runs on windows only — signtool and New-SelfSignedCertificate are Windows-only —
        // so, exactly as with the coverage floor, the verify step has to restate that condition
        // independently. Two statements a mutation must edit in both places.
        var verify = steps.Single(step => step.Lines.Any(line => IsUncommented(line, "ci-receipts.sh verify")));

        string.Join('\n', verify.Lines).Should().Contain(
            "signing-selftest",
            "the test job's verify step must demand the self-test's receipt; without it the step "
            + "could stop running and nothing would notice");
    }
}
