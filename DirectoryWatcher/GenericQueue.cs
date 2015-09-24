namespace Teamnet.DirectoryWatcher
{
	using System;
	using System.Diagnostics;
	using System.Collections;
	using System.Threading;

	public abstract class GenericQueue : IDisposable
	{	
		public readonly string Name;
		
		// threads is protected by queue.SyncRoot
		private ArrayList threads;
		private readonly int maxThreadsCount;
		private readonly int minThreadsCount;
		private readonly bool backgroundThread;
		// queue is protected by queue.SyncRoot
		private readonly Queue queue = new Queue();
		// waitingThreads is protected by queue.SyncRoot
		private readonly Stack waitingThreads;

		// these are really readonly :)
		protected int maximumWaitingItems = 2000;
		protected int millisecondsKeepAlive = 30000;
		
		public GenericQueue(string queueName) : this(queueName, false, 1, 1)
		{
		}

		public GenericQueue(string queueName, bool backgroundThread) : this(queueName, backgroundThread, 1, 1)
		{
		}

		public GenericQueue(string queueName, bool backgroundThread, byte maxThreadsCount, byte minThreadsCount)
		{
			if(minThreadsCount < 1 || maxThreadsCount < minThreadsCount)
			{
				throw new ArgumentException("1 <= minThreadsCount <= maxThreadsCount");
			}
			Name = string.Format("Q({0},{1}) - {2}", minThreadsCount, maxThreadsCount, queueName);
			threads = new ArrayList(maxThreadsCount);
			waitingThreads = new Stack(maxThreadsCount);
			this.backgroundThread = backgroundThread;
			this.maxThreadsCount = maxThreadsCount;
			this.minThreadsCount = minThreadsCount;
			for(int index = 0; index < minThreadsCount; index++)
			{
				AddNewThread();
			}
		}

		private void AddNewThread()
		{
			Thread thread = new Thread(new ThreadStart(ProcessQueue));
			thread.IsBackground = backgroundThread;			
			thread.Name = "Thread(" + thread.GetHashCode() + ")  " + Name;
			OnBeforeStartThread(thread);
			threads.Add(thread);
			thread.Start();
			Trace.WriteLine("[GenericQueue]: CREATED NEW THREAD: "+thread.Name);
		}

		public void Enqueue(object item)
		{
			Trace.WriteLine(string.Format("[GenericQueue] [COUNT: {0}]: Enqueue item to {1}. ThreadCount: {2}", queue.Count, Name, threads==null?0:threads.Count));
			lock(queue.SyncRoot)
			{
				if(threads == null)
				{
					return;
				}
				if(queue.Count == maximumWaitingItems)
				{
					OnFullQueue();
				}
				queue.Enqueue(item);
				// we are creating threads as more work comes in				
				// if there is some waiting thread then we don't need to create one more
				if(waitingThreads.Count > 0)
				{
					object threadLock = waitingThreads.Pop();
					lock(threadLock)
					{
						// let the waiting worker know there is work to be done
						Monitor.Pulse(threadLock);
					}
				}// if we reached maxThreadsCount then we are not allowed to create any more threads
				else if(threads.Count < maxThreadsCount)
				{
					AddNewThread();
				}
			}
		}
	
		public bool WaitForThread()
		{
			return WaitForThread(Timeout.Infinite);
		}

		public bool WaitForThread(int millisecondsTimeout)
		{
			lock(queue.SyncRoot)
			{
				if(threads == null)
				{
					return false;
				}
				// if there is some waiting thread then it will be released by an Enqueue
				// if we can create new threads then we will in Enqueue
				if(waitingThreads.Count > 0 || threads.Count < maxThreadsCount)
				{
					return true;
				}
				Trace.WriteLine(string.Format("[GenericQueue]: Waiting for free thread[{0}]___________________________________________", Name));
				// wait for a free thread
				return Monitor.Wait(queue.SyncRoot, millisecondsTimeout);
			}
		}

		/// <summary>
		///		Method that internal thread will execute.
		/// </summary>
		/// <remarks>
		///		While internal queue item is:
		///			- greather than zero, internal thread is process internal queue;
		///			- is equal o zero, internal thread is suspended.
		/// </remarks>
		private void ProcessQueue()
		{
			try
			{
				ProcessQueueInternal();
			}
			catch(ThreadInterruptedException)
			{
				// the queue was killed
			}
			catch(Exception ex)
			{
				Trace.WriteLine(ex);
			}
			Trace.WriteLine("[GenericQueue]: THREAD WAS KILLED: "+Thread.CurrentThread.Name);
		}

		private void ProcessQueueInternal()
		{
			int timeout;
			object localLock = new object();
			while(true)
			{
				ProcessItems();
				
				Monitor.Enter(queue.SyncRoot);
				try
				{
					if(threads == null)
					{
						Monitor.Exit(queue.SyncRoot);
						return;
					}
					if(queue.Count > 0)
					{
						Monitor.Exit(queue.SyncRoot);
						continue;	// process the items in the queue
					}
					waitingThreads.Push(localLock);
					// let them know there is a free thread
					Monitor.Pulse(queue.SyncRoot);
					timeout = (threads.Count <= minThreadsCount) ? Timeout.Infinite : millisecondsKeepAlive;
				}
				catch
				{
					Monitor.Exit(queue.SyncRoot);
					throw;
				}
				lock(localLock)
				{
					Monitor.Exit(queue.SyncRoot);
					Trace.WriteLine(string.Format("[GenericQueue]: Waiting in Queue.ProcessQueue[{0}]___________________________________________", Thread.CurrentThread.Name));
					// wait for more work to come in
					while(!Monitor.Wait(localLock, timeout))
					{
						// tired of waiting; give up and finish the thread; when we have more work we can create more threads
						lock(queue.SyncRoot)
						{
							if(threads == null)
							{
								return;
							}
							if(threads.Count > minThreadsCount)
							{
								threads.Remove(Thread.CurrentThread);
								RemoveWaitingThread(localLock);
								return;
							}
						}
						timeout = Timeout.Infinite;
					}
					Trace.WriteLine(string.Format("[GenericQueue]: Wake-up thread {0} - Count = {1}.", Thread.CurrentThread.Name, queue.Count));
				}
			}
		}

		private void RemoveWaitingThread(object localLock)
		{
			object[] old = waitingThreads.ToArray();
			waitingThreads.Clear();
			for(int index = old.Length - 1; index >= 0; index--)
			{
				if(old[index] != localLock)
				{
					waitingThreads.Push(old[index]);
				}
			}
		}

		public void Kill()
		{
			lock(queue.SyncRoot)
			{
				if(threads == null)
				{
					return;
				}
				queue.Clear();
				foreach(Thread thread in threads)
				{
					thread.Interrupt();
				}
				threads = null;
			}
		}
		
		// this function runs inside a lock for the queue
		protected virtual void OnFullQueue()
		{
			Trace.WriteLine(new ApplicationException(string.Format("[GenericQueue]: THE QUEUE {0} HAS {1} ITEMS AND IT WILL BE CLEARED!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!", Name, queue.Count)));
			queue.Clear();
		}

		// this function runs inside a lock for the queue
		protected virtual void OnBeforeStartThread(Thread thread)
		{
		}

		private void ProcessItems()
		{
			// this is more like a hint; we'll have a real count after we take the lock
			while(queue.Count > 0)
			{
				object item = null;
				lock(queue.SyncRoot)
				{
					if(queue.Count > 0)
					{
						item = queue.Dequeue();
					}
				}
				if(item == null)
				{
					continue;	// try again
				}
				try
				{
					Trace.WriteLine(string.Format("[GenericQueue]: BEGIN processing item from queue {0} - Count = {1}.", Thread.CurrentThread.Name, queue.Count));
					ProcessQueueItem(item);
					Trace.WriteLine(string.Format("[GenericQueue]: END processing item from queue {0}.", Thread.CurrentThread.Name));
				}
				catch(Exception ex)
				{
					Trace.WriteLine(ex);
				}
			}
		}

		abstract protected void ProcessQueueItem(object item);

		protected virtual void Dispose(bool disposing)
		{
			if(disposing)
			{
				Kill();
			}
		}

		#region IDisposable Members

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		#endregion
	}// class GenericQueue
}