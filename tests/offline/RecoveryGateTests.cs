using System;

namespace MPFever
{
    // A failed resynchronisation used to refuse every resume (host and clients,
    // launcher and game buttons) until the whole session was restarted.
    static partial class OfflineTests
    {
        static void RecoveryGateTests()
        {
            var gate=new RecoveryGate();bool overridden;
            Check(gate.Resume(false,false,out overridden)==null && !overridden,"healthy session resumes for any player");
            Check(gate.Resume(true,true,out overridden)!=null,"nobody resumes while a resynchronisation runs");
            gate.Freeze();
            Check(gate.Resume(false,false,out overridden)!=null,"a running recovery that failed keeps clients from resuming");
            Check(gate.Failure(100) && gate.RetryAt==100+RecoveryGate.RetrySeconds,"first failure schedules an automatic retry");
            Check(!gate.Due(100+RecoveryGate.RetrySeconds-1) && gate.Due(100+RecoveryGate.RetrySeconds),"retry starts when due");
            Check(!gate.Due(1e9),"a retry is taken once");
            Check(gate.Failure(200) && gate.Failure(300),"second and third failures retry too");
            Check(!gate.Failure(400) && gate.RetryAt<0 && gate.Failed,"retries are bounded; the games stay paused");
            Check(gate.Resume(false,false,out overridden)!=null,"after the retries a client still cannot resume");
            Check(gate.Resume(false,true,out overridden)==null && overridden && !gate.Failed && gate.Failures==0,"the host can always resume after a failure");
            Check(gate.Resume(false,false,out overridden)==null && !overridden,"after the host's decision every player controls speed again");
            gate.Request(50);gate.Request(80);
            Check(gate.RetryAt==50 && gate.Due(60),"a recovery asked for during the cooldown is kept, not dropped");
            gate.Freeze();gate.Clear();
            Check(!gate.Failed && gate.RetryAt<0 && gate.Resume(false,false,out overridden)==null,"success or an empty session clears the failure");
        }
    }
}
