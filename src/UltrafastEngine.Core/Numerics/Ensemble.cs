// Ensemble.cs -- batched study points on a device: the propagations of many study points are collected and run as one ensemble
// (one kernel launch covers every point), the good case for GPUs on 1-D problems, which are launch-latency bound alone.
//
// How it works: Studies.Run runs the points of a batch on their own threads, each registered with one EnsembleGate
// (EnsembleGate.Current, thread-static). When a point's propagation reaches a back-end that implements IEnsembleBackend and the
// back-end gives the problem an ensemble key, the job is submitted to the gate and the thread waits. As soon as every registered
// thread is waiting (or has finished), the last one to arrive runs all pending jobs, grouped by key (only problems with the same
// shape, grid and stepper settings share a launch), and wakes the others. Points therefore advance element by element together;
// nothing is reordered, and every member is integrated exactly as it would be alone (its own step sizes), so results equal a single
// run's to rounding.
//
// Classes: EnsembleJob, IEnsembleBackend (EnsembleKey, PropagateEnsemble), EnsembleGate (Enter, Leave, Submit, Current, counters)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using UltrafastEngine.Physics;

namespace UltrafastEngine.Numerics
{
    /// <summary>One propagation waiting in an ensemble.</summary>
    public sealed class EnsembleJob
    {
        public IPropagationProblem Problem;
        public Complex[] Initial;
        public StepperSettings Settings;
        public StepStatistics Stats;
        public Action<double> Progress;
        public string Key;
        public Complex[] Result;
        public Exception Error;
        internal bool Done;
    }

    /// <summary>A back-end that can propagate several problems of the same kind in one launch.</summary>
    public interface IEnsembleBackend : IDeviceBackend
    {
        /// <summary>A key: problems with equal keys can share one ensemble launch. Null: run this problem alone.</summary>
        string EnsembleKey(IPropagationProblem problem, StepperSettings settings);
        /// <summary>Propagate the jobs (all with the same key) and set each job's Result (or Error).</summary>
        void PropagateEnsemble(IList<EnsembleJob> jobs);
    }

    /// <summary>The meeting point of the threads of one study batch (see the file header).</summary>
    public sealed class EnsembleGate
    {
        [ThreadStatic] private static EnsembleGate _current;
        /// <summary>The gate of the calling thread's study point, or null (no ensemble).</summary>
        public static EnsembleGate Current { get => _current; set => _current = value; }

        private readonly object _lock = new object();
        private readonly List<EnsembleJob> _pending = new List<EnsembleJob>();
        private int _active;
        private bool _running;

        /// <summary>Largest number of members per launch (the study's "ensemble"); 0: the back-end's default.</summary>
        public int MaxMembers { get; set; }

        /// <summary>Ensemble launches made, members propagated in them, and the largest ensemble.</summary>
        public int Launches { get; private set; }
        public int Members { get; private set; }
        public int Largest { get; private set; }

        /// <summary>Register the calling thread (a study point that has started).</summary>
        public void Enter() { lock (_lock) _active++; }

        /// <summary>Unregister the calling thread (its point has finished); runs the pending jobs if everyone left is waiting.</summary>
        public void Leave()
        {
            List<EnsembleJob> batch;
            lock (_lock) { _active--; batch = Take(); }
            if (batch != null) RunBatch(batch);
        }

        /// <summary>Queue a job and wait until it has been propagated (by this thread or another).</summary>
        public Complex[] Submit(EnsembleJob job, IEnsembleBackend backend)
        {
            job.Key = job.Key ?? backend.EnsembleKey(job.Problem, job.Settings);
            List<EnsembleJob> batch;
            lock (_lock)
            {
                _backend = backend;
                _pending.Add(job);
                batch = Take();
            }
            if (batch != null) RunBatch(batch);
            lock (_lock) { while (!job.Done) Monitor.Wait(_lock); }
            if (job.Error != null) throw new InvalidOperationException("ensemble propagation failed: " + job.Error.Message, job.Error);
            return job.Result;
        }

        private IEnsembleBackend _backend;

        /// <summary>The pending jobs when every active thread is waiting (and no batch is running); else null. Call under the lock.</summary>
        private List<EnsembleJob> Take()
        {
            if (_running || _pending.Count == 0 || _pending.Count < _active) return null;
            var b = new List<EnsembleJob>(_pending);
            _pending.Clear();
            _running = true;
            return b;
        }

        private void RunBatch(List<EnsembleJob> batch)
        {
            try
            {
                foreach (var g in batch.GroupBy(j => j.Key))
                {
                    var jobs = g.ToList();
                    try { _backend.PropagateEnsemble(jobs); }
                    catch (Exception ex) { foreach (var j in jobs) if (j.Result == null) j.Error = ex; }
                    lock (_lock) { Launches++; Members += jobs.Count; Largest = Math.Max(Largest, jobs.Count); }
                }
            }
            finally
            {
                lock (_lock)
                {
                    foreach (var j in batch) j.Done = true;
                    _running = false;
                    Monitor.PulseAll(_lock);
                }
            }
        }
    }
}
