using Xunit;

namespace Pine.Tests.Unit.Composition;

/// <summary>
/// Serialises the three test classes that touch process-wide state: the real Paddle native stack,
/// and <c>WeakReferenceMessenger.Default</c>.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS TD-117's SECOND HALF, AND IT IS NOT A FLAKY TEST. The unit suite aborted roughly one run
/// in two -- ten observations in a single day were recorded in iteration 24, and it has cost a
/// re-run in every iteration since. Iteration 26 captured the crash and reproduced it three times
/// consecutively:
/// </para>
/// <code>
/// Fatal error. 0xC0000005
///    at Sdcb.PaddleInference.Native.PaddleNative.PD_PredictorCreate2(IntPtr, IntPtr)
///    at Sdcb.PaddleOCR.PaddleOcrAll..ctor(...)
///    at Pine.Infrastructure.Ai.Vision.PaddleOcrEngine.Initialize()
///    at Pine.Infrastructure.Ai.DefaultLocalAiProvider..ctor(...)
/// </code>
/// <para>
/// It is a native access violation, not an assertion: xunit runs test CLASSES in parallel, three
/// classes here each build the Windows profile's container, and <c>PaddleOcrEngine</c>'s
/// constructor creates a Paddle predictor. Three threads entering <c>PD_PredictorCreate2</c> at
/// once takes the whole test host down, which is why the run reports a passing count and then
/// "test run aborted" -- there is no failing test to name, because the process died mid-collection.
/// </para>
/// <para>
/// A killed host cannot be diagnosed by the suite that died with it, which is why every test count
/// in <c>60-Production-Readiness-Audit</c> since iteration 24 cost two or three runs to obtain.
/// Serialising the three classes is the fix iteration 26 already identified and deferred. It costs
/// parallelism across three classes and buys a measurable suite.
/// </para>
/// <para>
/// WHY NOT SUBSTITUTE THE PROVIDER INSTEAD. Because these tests exist to prove the head's REAL
/// graph closes -- <c>ValidateOnBuild</c> over the container a user actually gets. Replacing
/// <c>IAIProvider</c> with a substitute would remove the very thing under test and would have
/// quietly deleted the coverage that found TD-90.
/// </para>
/// <para>
/// IT ABSORBS THE OLD "Messenger" COLLECTION, which existed for the second hazard and named it
/// well: <c>WeakReferenceMessenger.Default</c> is a process-wide singleton, so a recipient
/// activated by one class receives broadcasts sent by another and the "exactly once" assertions
/// count deliveries they never sent. That is the same category of problem as the predictor -- one
/// process-wide resource, several classes, run concurrently -- so it is one collection, named for
/// the category rather than for either instance. A class can only belong to one collection, which
/// is what forced the merge: <c>MessengerLifecycleTests</c> builds the Windows container too, so it
/// needed both guarantees at once.
/// </para>
/// </remarks>
[CollectionDefinition("ProcessWideState", DisableParallelization = true)]
public sealed class ProcessWideStateTestGroup
{
}
