using System;
using System.Threading.Tasks;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Work done off the main thread where only the newest request matters (0.12.63; the owner: "加载参数显示会卡住主线程"): the editor's picture of a board was made on the main thread at every
    /// change of a setting (layout, painting, PNG: 11 ms on the median board of the catalogue, 61 ms for a prohibition sign, 96 ms for a lantern box in the bench). Submit gives the job; one task
    /// at a time makes them, and a job that is overtaken by a newer one before it starts is dropped, so that typing queues nothing. The result of the last job done is taken from the main thread
    /// (TryTake), once. Pure.
    /// </summary>
    public sealed class LatestWorker<TJob, TResult>
    {
        private readonly Func<TJob, TResult> m_Work;
        private readonly Action<Exception> m_OnError;
        private readonly object m_Lock = new object();
        private TJob m_Next;
        private bool m_HasNext, m_Running, m_HasResult;
        private TResult m_Result;

        /// <summary>How many jobs were made (the others were overtaken or failed).</summary>
        public int Made { get; private set; }

        public LatestWorker(Func<TJob, TResult> work, Action<Exception> onError = null)
        {
            m_Work = work;
            m_OnError = onError;
        }

        /// <summary>A job to be made; it replaces one that has not started.</summary>
        public void Submit(TJob job)
        {
            lock (m_Lock)
            {
                m_Next = job;
                m_HasNext = true;
                if (m_Running)
                    return;
                m_Running = true;
            }

            Task.Run((Action)Loop);
        }

        /// <summary>The result of the last job made, if it was not taken yet.</summary>
        public bool TryTake(out TResult result)
        {
            lock (m_Lock)
            {
                result = m_Result;
                bool had = m_HasResult;
                m_Result = default;
                m_HasResult = false;
                return had;
            }
        }

        private void Loop()
        {
            while (true)
            {
                TJob job;
                lock (m_Lock)
                {
                    if (!m_HasNext)
                    {
                        m_Running = false;
                        return;
                    }

                    job = m_Next;
                    m_Next = default;
                    m_HasNext = false;
                }

                TResult result;
                try
                {
                    result = m_Work(job);
                }
                catch (Exception ex)
                {
                    m_OnError?.Invoke(ex);
                    continue;
                }

                lock (m_Lock)
                {
                    m_Result = result;
                    m_HasResult = true;
                    Made++;
                }
            }
        }
    }
}
