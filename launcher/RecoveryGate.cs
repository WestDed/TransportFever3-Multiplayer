namespace MPFever
{
    // After a failed resynchronisation the games stay paused, but never for good
    // (before, every resume was refused until the session was restarted): the
    // recovery is retried a few times while clients remain, the host may always
    // resume by itself, and a host left alone plays on.
    sealed class RecoveryGate
    {
        public const int Retries = 3;
        public const double RetrySeconds = 30;
        readonly object gate = new object();
        bool failed;
        int failures;
        double retryAt = -1;
        public bool Failed { get { lock (gate) return failed; } }
        public int Failures { get { lock (gate) return failures; } }
        public double RetryAt { get { lock (gate) return retryAt; } }

        /// <summary>The games were paused for a recovery: until it succeeds, only the host resumes them.</summary>
        public void Freeze() { lock (gate) failed = true; }

        /// <summary>Recovered, or nobody left to differ from.</summary>
        public void Clear() { lock (gate) { failed = false; failures = 0; retryAt = -1; } }

        /// <summary>Null when a resume may proceed, otherwise why it is refused. A host's resume after a failure
        /// clears it (overridden).</summary>
        public string Resume(bool recovering, bool host, out bool overridden)
        {
            overridden = false;
            lock (gate)
            {
                if (recovering) return "a resynchronisation is running; it resumes the game itself when it ends";
                if (!failed) return null;
                if (!host) return "the last resynchronisation failed: the host resumes (its speed buttons) or the automatic retry recovers";
                failed = false; failures = 0; retryAt = -1; overridden = true;
                return null;
            }
        }

        /// <summary>A recovery that paused the games failed. True: a retry is due at RetryAt.</summary>
        public bool Failure(double now)
        {
            lock (gate)
            {
                failed = true;
                if (++failures > Retries) { retryAt = -1; return false; }
                retryAt = now + RetrySeconds;
                return true;
            }
        }

        /// <summary>A recovery asked for during the cooldown (a build not reproduced) is kept for later, not dropped:
        /// the game that asked holds every other game behind it until it is reloaded.</summary>
        public void Request(double at) { lock (gate) if (retryAt < 0 || at < retryAt) retryAt = at; }

        /// <summary>Takes the pending retry when it is due (the caller checked the session can run one).</summary>
        public bool Due(double now)
        {
            lock (gate)
            {
                if (retryAt < 0 || now < retryAt) return false;
                retryAt = -1;
                return true;
            }
        }
    }
}
