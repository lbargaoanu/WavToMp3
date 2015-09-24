namespace Teamnet.DirectoryWatcher
{
    using System.Threading;
    using System.Collections.Specialized;
    using System;

    public sealed class JobsRunner
	{
		private static readonly WorkItemsQueue queue = new WorkItemsQueue();
		private static readonly StringCollection runningItems = new StringCollection();

		private JobsRunner()
		{
		}

		public static string[] GetRunningItems()
		{
			lock(runningItems.SyncRoot)
			{
				string[] items = new string[runningItems.Count];
				runningItems.CopyTo(items, 0);
				return items;
			}
		}

		public static void QueueUserWorkItem(WaitCallback callBack)
		{
			QueueUserWorkItem(callBack, null, null);
		}

		public static void QueueUserWorkItem(WaitCallback callBack, object state)
		{
			QueueUserWorkItem(callBack, state, null);
		}

		public static void QueueUserWorkItem(WaitCallback callBack, object state, string key)
		{
			if(key != null && key.Length > 0)
			{
				callBack = new WaitCallback(new RunHelper(key, callBack).RunItem);
				lock(JobsRunner.runningItems.SyncRoot)
				{
					runningItems.Add(key);
				}
			}
			queue.Enqueue(new WorkItem(callBack, state));
		}

		private class RunHelper
		{
			private WaitCallback callBack;
			private string key;

			public RunHelper(string key, WaitCallback callBack)
			{
				this.key = key;
				this.callBack = callBack;				
			}

			public void RunItem(object state)
			{
				try
				{
					callBack(state);
				}
				finally
				{
					lock(JobsRunner.runningItems.SyncRoot)
					{
						JobsRunner.runningItems.Remove(key);
					}
				}
			}
		}

		private sealed class WorkItem
		{
			public WorkItem(WaitCallback callBack, object state)
			{
				CallBack = callBack;
				State = state;
			}
			public readonly object State;
			public readonly WaitCallback CallBack;
		}

		private sealed class WorkItemsQueue : GenericQueue
		{
			public WorkItemsQueue() : base("WorkItemsQueue", 
                                                            backgroundThread: true, 
                                                            maxThreadsCount : Utils.GetSetting("maxThreads", (byte) Environment.ProcessorCount),
                                                            minThreadsCount : Utils.GetSetting("minThreads", (byte) 1))
			{
			}

			protected sealed override void ProcessQueueItem(object item)
			{
				var workItem = (WorkItem) item;
				workItem.CallBack(workItem.State);
			}
		}
	}
}