using NUnit.Framework;
using EOSNative.Net;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Tests
{
    public class SyncTimerTests
    {
        [TearDown]
        public void TearDown()
        {
            foreach (var name in new[] { "NetworkManager", "EOSP2PManager", "EOSLobbyManager", "NetworkSceneManager" })
            {
                var obj = GameObject.Find(name);
                if (obj != null) Object.DestroyImmediate(obj);
            }
        }

        private (NetworkObject netObj, SyncTimer timer) CreateTimer(float duration = 0f)
        {
            var go = new GameObject("TestNetObj");
            var netObj = go.AddComponent<NetworkObject>();
            var timer = netObj.SyncTimer(duration);
            return (netObj, timer);
        }

        private (NetworkObject netObj, SyncStopwatch sw) CreateStopwatch()
        {
            var go = new GameObject("TestNetObj");
            var netObj = go.AddComponent<NetworkObject>();
            var sw = netObj.SyncStopwatch();
            return (netObj, sw);
        }

        private void Cleanup(NetworkObject netObj)
        {
            Object.DestroyImmediate(netObj.gameObject);
        }

        // ─── SyncTimer Tests ───

        #region SyncTimer_Basics

        [Test]
        public void Timer_InitialDuration()
        {
            var (netObj, timer) = CreateTimer(10f);
            Assert.AreEqual(10f, timer.Remaining, 0.0001f);
            Assert.IsFalse(timer.IsRunning);
            Assert.IsFalse(timer.IsExpired);
            Cleanup(netObj);
        }

        [Test]
        public void Timer_DefaultDurationZero()
        {
            var (netObj, timer) = CreateTimer();
            Assert.AreEqual(0f, timer.Remaining, 0.0001f);
            Assert.IsTrue(timer.IsExpired);
            Cleanup(netObj);
        }

        [Test]
        public void Timer_Start_SetsRunningAndDuration()
        {
            var (netObj, timer) = CreateTimer();
            timer.Start(5f);
            Assert.AreEqual(5f, timer.Remaining, 0.0001f);
            Assert.IsTrue(timer.IsRunning);
            Assert.IsFalse(timer.IsExpired);
            Cleanup(netObj);
        }

        [Test]
        public void Timer_Start_MarksDirty()
        {
            var (netObj, timer) = CreateTimer();
            Assert.IsFalse(timer.IsDirty);
            timer.Start(5f);
            Assert.IsTrue(timer.IsDirty);
            Cleanup(netObj);
        }

        [Test]
        public void Timer_Tick_CountsDown()
        {
            var (netObj, timer) = CreateTimer();
            timer.Start(10f);
            timer.ClearDirty();

            timer.Tick(3f);
            Assert.AreEqual(7f, timer.Remaining, 0.0001f);
            Assert.IsTrue(timer.IsRunning);
            Assert.IsTrue(timer.IsDirty);
            Cleanup(netObj);
        }

        [Test]
        public void Timer_Tick_ClampsToZero()
        {
            var (netObj, timer) = CreateTimer();
            timer.Start(2f);
            timer.Tick(5f); // overshoot
            Assert.AreEqual(0f, timer.Remaining, 0.0001f);
            Assert.IsTrue(timer.IsExpired);
            Assert.IsFalse(timer.IsRunning); // auto-stops on expire
            Cleanup(netObj);
        }

        [Test]
        public void Timer_Tick_NotRunning_DoesNothing()
        {
            var (netObj, timer) = CreateTimer(10f);
            // Not started (not running)
            timer.Tick(5f);
            Assert.AreEqual(10f, timer.Remaining, 0.0001f);
            Cleanup(netObj);
        }

        [Test]
        public void Timer_Tick_AlreadyExpired_DoesNothing()
        {
            var (netObj, timer) = CreateTimer();
            timer.Start(1f);
            timer.Tick(2f); // expires
            timer.ClearDirty();

            timer.Tick(1f); // should do nothing since remaining=0
            Assert.IsFalse(timer.IsDirty);
            Cleanup(netObj);
        }

        #endregion

        #region SyncTimer_PauseResumeReset

        [Test]
        public void Timer_Pause_StopsCountdown()
        {
            var (netObj, timer) = CreateTimer();
            timer.Start(10f);
            timer.Tick(3f); // remaining=7
            timer.Pause();

            Assert.IsFalse(timer.IsRunning);
            Assert.AreEqual(7f, timer.Remaining, 0.0001f);

            timer.Tick(5f); // should do nothing while paused
            Assert.AreEqual(7f, timer.Remaining, 0.0001f);
            Cleanup(netObj);
        }

        [Test]
        public void Timer_Resume_ContinuesCountdown()
        {
            var (netObj, timer) = CreateTimer();
            timer.Start(10f);
            timer.Tick(3f); // 7
            timer.Pause();
            timer.Resume();

            Assert.IsTrue(timer.IsRunning);
            timer.Tick(2f);
            Assert.AreEqual(5f, timer.Remaining, 0.0001f);
            Cleanup(netObj);
        }

        [Test]
        public void Timer_Reset_SetsZeroAndStops()
        {
            var (netObj, timer) = CreateTimer();
            timer.Start(10f);
            timer.Tick(3f); // 7
            timer.Reset();

            Assert.AreEqual(0f, timer.Remaining, 0.0001f);
            Assert.IsFalse(timer.IsRunning);
            Assert.IsTrue(timer.IsExpired);
            Cleanup(netObj);
        }

        [Test]
        public void Timer_Start_ResetsRunningTimer()
        {
            var (netObj, timer) = CreateTimer();
            timer.Start(10f);
            timer.Tick(5f); // 5
            timer.Start(20f); // restart with new duration

            Assert.AreEqual(20f, timer.Remaining, 0.0001f);
            Assert.IsTrue(timer.IsRunning);
            Cleanup(netObj);
        }

        #endregion

        #region SyncTimer_Events

        [Test]
        public void Timer_OnChanged_FiresOnStart()
        {
            var (netObj, timer) = CreateTimer();
            float capturedOld = -1f, capturedNew = -1f;
            timer.OnChanged += (old, @new) => { capturedOld = old; capturedNew = @new; };

            timer.Start(5f);
            Assert.AreEqual(0f, capturedOld, 0.0001f);
            Assert.AreEqual(5f, capturedNew, 0.0001f);
            Cleanup(netObj);
        }

        [Test]
        public void Timer_OnChanged_FiresOnTick()
        {
            var (netObj, timer) = CreateTimer();
            timer.Start(10f);

            float capturedOld = -1f, capturedNew = -1f;
            timer.OnChanged += (old, @new) => { capturedOld = old; capturedNew = @new; };

            timer.Tick(3f);
            Assert.AreEqual(10f, capturedOld, 0.0001f);
            Assert.AreEqual(7f, capturedNew, 0.0001f);
            Cleanup(netObj);
        }

        [Test]
        public void Timer_OnExpired_FiresWhenReachesZero()
        {
            var (netObj, timer) = CreateTimer();
            timer.Start(2f);

            bool expired = false;
            timer.OnExpired += () => expired = true;

            timer.Tick(1f); // 1 remaining
            Assert.IsFalse(expired);

            timer.Tick(2f); // 0 remaining
            Assert.IsTrue(expired);
            Cleanup(netObj);
        }

        [Test]
        public void Timer_OnExpired_FiresOnlyOnce()
        {
            var (netObj, timer) = CreateTimer();
            timer.Start(1f);

            int expireCount = 0;
            timer.OnExpired += () => expireCount++;

            timer.Tick(2f); // expires
            Assert.AreEqual(1, expireCount);

            // Tick again — already expired, should not fire
            timer.Tick(1f);
            Assert.AreEqual(1, expireCount);
            Cleanup(netObj);
        }

        #endregion

        #region SyncTimer_Serialization

        [Test]
        public void Timer_WriteTo_ReadFrom_RoundTrip()
        {
            var (netObj1, timer1) = CreateTimer();
            timer1.Start(7.5f);
            timer1.Tick(2f); // remaining=5.5, running=true

            var writer = new NetWriter();
            timer1.WriteTo(writer);

            var (netObj2, timer2) = CreateTimer();
            var reader = new NetReader(writer.ToArray());
            timer2.ReadFrom(reader);

            Assert.AreEqual(timer1.Remaining, timer2.Remaining, 0.0001f);
            Assert.AreEqual(timer1.IsRunning, timer2.IsRunning);

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        [Test]
        public void Timer_WriteFullState_ReadFullState_RoundTrip()
        {
            var (netObj1, timer1) = CreateTimer();
            timer1.Start(3f);
            timer1.Pause(); // remaining=3, running=false

            var writer = new NetWriter();
            timer1.WriteFullState(writer);

            var (netObj2, timer2) = CreateTimer();
            var reader = new NetReader(writer.ToArray());
            timer2.ReadFullState(reader);

            Assert.AreEqual(3f, timer2.Remaining, 0.0001f);
            Assert.IsFalse(timer2.IsRunning);

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        [Test]
        public void Timer_ReadFrom_FiresOnChanged()
        {
            var (netObj1, timer1) = CreateTimer();
            timer1.Start(10f);

            var writer = new NetWriter();
            timer1.WriteTo(writer);

            var (netObj2, timer2) = CreateTimer();
            float capturedOld = -1f, capturedNew = -1f;
            timer2.OnChanged += (old, @new) => { capturedOld = old; capturedNew = @new; };

            var reader = new NetReader(writer.ToArray());
            timer2.ReadFrom(reader);

            Assert.AreEqual(0f, capturedOld, 0.0001f); // timer2 was 0
            Assert.AreEqual(10f, capturedNew, 0.0001f);

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        [Test]
        public void Timer_ReadFrom_FiresOnExpired_WhenCrossesZero()
        {
            var (netObj1, timer1) = CreateTimer();
            // timer1 is at 0 (expired)
            var writer = new NetWriter();
            timer1.WriteTo(writer);

            var (netObj2, timer2) = CreateTimer(5f);
            // timer2 is at 5 (not expired)
            bool expired = false;
            timer2.OnExpired += () => expired = true;

            var reader = new NetReader(writer.ToArray());
            timer2.ReadFrom(reader); // reads 0, was 5 → should fire OnExpired

            Assert.IsTrue(expired);

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        #endregion

        #region SyncTimer_ClearDirty

        [Test]
        public void Timer_ClearDirty()
        {
            var (netObj, timer) = CreateTimer();
            timer.Start(5f);
            Assert.IsTrue(timer.IsDirty);
            timer.ClearDirty();
            Assert.IsFalse(timer.IsDirty);
            Cleanup(netObj);
        }

        #endregion

        #region SyncTimer_WriteAccess

        [Test]
        public void Timer_WriteAccess_DefaultOwner()
        {
            var (netObj, timer) = CreateTimer();
            Assert.AreEqual(SyncVarWriteAccess.Owner, timer.WriteAccess);
            Cleanup(netObj);
        }

        #endregion

        // ─── SyncStopwatch Tests ───

        #region SyncStopwatch_Basics

        [Test]
        public void Stopwatch_InitialState()
        {
            var (netObj, sw) = CreateStopwatch();
            Assert.AreEqual(0f, sw.Elapsed, 0.0001f);
            Assert.IsFalse(sw.IsRunning);
            Cleanup(netObj);
        }

        [Test]
        public void Stopwatch_Start_SetsRunning()
        {
            var (netObj, sw) = CreateStopwatch();
            sw.Start();
            Assert.IsTrue(sw.IsRunning);
            Assert.AreEqual(0f, sw.Elapsed, 0.0001f);
            Cleanup(netObj);
        }

        [Test]
        public void Stopwatch_Start_MarksDirty()
        {
            var (netObj, sw) = CreateStopwatch();
            Assert.IsFalse(sw.IsDirty);
            sw.Start();
            Assert.IsTrue(sw.IsDirty);
            Cleanup(netObj);
        }

        [Test]
        public void Stopwatch_Tick_AccumulatesTime()
        {
            var (netObj, sw) = CreateStopwatch();
            sw.Start();
            sw.Tick(2f);
            Assert.AreEqual(2f, sw.Elapsed, 0.0001f);
            sw.Tick(3f);
            Assert.AreEqual(5f, sw.Elapsed, 0.0001f);
            Cleanup(netObj);
        }

        [Test]
        public void Stopwatch_Tick_NotRunning_DoesNothing()
        {
            var (netObj, sw) = CreateStopwatch();
            sw.Tick(5f);
            Assert.AreEqual(0f, sw.Elapsed, 0.0001f);
            Cleanup(netObj);
        }

        #endregion

        #region SyncStopwatch_StopResetRestart

        [Test]
        public void Stopwatch_Stop_PreservesElapsed()
        {
            var (netObj, sw) = CreateStopwatch();
            sw.Start();
            sw.Tick(5f);
            sw.Stop();

            Assert.IsFalse(sw.IsRunning);
            Assert.AreEqual(5f, sw.Elapsed, 0.0001f);

            sw.Tick(3f); // should not accumulate while stopped
            Assert.AreEqual(5f, sw.Elapsed, 0.0001f);
            Cleanup(netObj);
        }

        [Test]
        public void Stopwatch_Reset_ZerosAndStops()
        {
            var (netObj, sw) = CreateStopwatch();
            sw.Start();
            sw.Tick(5f);
            sw.Reset();

            Assert.AreEqual(0f, sw.Elapsed, 0.0001f);
            Assert.IsFalse(sw.IsRunning);
            Cleanup(netObj);
        }

        [Test]
        public void Stopwatch_Restart_ZerosAndStarts()
        {
            var (netObj, sw) = CreateStopwatch();
            sw.Start();
            sw.Tick(5f);
            sw.Restart();

            Assert.AreEqual(0f, sw.Elapsed, 0.0001f);
            Assert.IsTrue(sw.IsRunning);

            sw.Tick(2f);
            Assert.AreEqual(2f, sw.Elapsed, 0.0001f);
            Cleanup(netObj);
        }

        [Test]
        public void Stopwatch_StartAfterStop_Accumulates()
        {
            var (netObj, sw) = CreateStopwatch();
            sw.Start();
            sw.Tick(3f);
            sw.Stop();
            sw.Start(); // resume
            sw.Tick(2f);
            Assert.AreEqual(5f, sw.Elapsed, 0.0001f);
            Cleanup(netObj);
        }

        #endregion

        #region SyncStopwatch_Events

        [Test]
        public void Stopwatch_OnChanged_FiresOnTick()
        {
            var (netObj, sw) = CreateStopwatch();
            sw.Start();

            float capturedOld = -1f, capturedNew = -1f;
            sw.OnChanged += (old, @new) => { capturedOld = old; capturedNew = @new; };

            sw.Tick(3f);
            Assert.AreEqual(0f, capturedOld, 0.0001f);
            Assert.AreEqual(3f, capturedNew, 0.0001f);
            Cleanup(netObj);
        }

        [Test]
        public void Stopwatch_OnChanged_FiresOnReset()
        {
            var (netObj, sw) = CreateStopwatch();
            sw.Start();
            sw.Tick(5f);

            float capturedOld = -1f, capturedNew = -1f;
            sw.OnChanged += (old, @new) => { capturedOld = old; capturedNew = @new; };

            sw.Reset();
            Assert.AreEqual(5f, capturedOld, 0.0001f);
            Assert.AreEqual(0f, capturedNew, 0.0001f);
            Cleanup(netObj);
        }

        #endregion

        #region SyncStopwatch_Serialization

        [Test]
        public void Stopwatch_WriteTo_ReadFrom_RoundTrip()
        {
            var (netObj1, sw1) = CreateStopwatch();
            sw1.Start();
            sw1.Tick(7.5f);

            var writer = new NetWriter();
            sw1.WriteTo(writer);

            var (netObj2, sw2) = CreateStopwatch();
            var reader = new NetReader(writer.ToArray());
            sw2.ReadFrom(reader);

            Assert.AreEqual(sw1.Elapsed, sw2.Elapsed, 0.0001f);
            Assert.AreEqual(sw1.IsRunning, sw2.IsRunning);

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        [Test]
        public void Stopwatch_WriteFullState_ReadFullState_RoundTrip()
        {
            var (netObj1, sw1) = CreateStopwatch();
            sw1.Start();
            sw1.Tick(4f);
            sw1.Stop();

            var writer = new NetWriter();
            sw1.WriteFullState(writer);

            var (netObj2, sw2) = CreateStopwatch();
            var reader = new NetReader(writer.ToArray());
            sw2.ReadFullState(reader);

            Assert.AreEqual(4f, sw2.Elapsed, 0.0001f);
            Assert.IsFalse(sw2.IsRunning);

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        [Test]
        public void Stopwatch_ReadFrom_FiresOnChanged()
        {
            var (netObj1, sw1) = CreateStopwatch();
            sw1.Start();
            sw1.Tick(8f);

            var writer = new NetWriter();
            sw1.WriteTo(writer);

            var (netObj2, sw2) = CreateStopwatch();
            float capturedOld = -1f, capturedNew = -1f;
            sw2.OnChanged += (old, @new) => { capturedOld = old; capturedNew = @new; };

            var reader = new NetReader(writer.ToArray());
            sw2.ReadFrom(reader);

            Assert.AreEqual(0f, capturedOld, 0.0001f);
            Assert.AreEqual(8f, capturedNew, 0.0001f);

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        #endregion

        #region SyncStopwatch_ClearDirty

        [Test]
        public void Stopwatch_ClearDirty()
        {
            var (netObj, sw) = CreateStopwatch();
            sw.Start();
            Assert.IsTrue(sw.IsDirty);
            sw.ClearDirty();
            Assert.IsFalse(sw.IsDirty);
            Cleanup(netObj);
        }

        #endregion

        #region SyncStopwatch_WriteAccess

        [Test]
        public void Stopwatch_WriteAccess_DefaultOwner()
        {
            var (netObj, sw) = CreateStopwatch();
            Assert.AreEqual(SyncVarWriteAccess.Owner, sw.WriteAccess);
            Cleanup(netObj);
        }

        #endregion

        #region ISyncVar_Contract

        [Test]
        public void Timer_ImplementsISyncVar()
        {
            var (netObj, timer) = CreateTimer(5f);
            Assert.IsInstanceOf<ISyncVar>(timer);
            Cleanup(netObj);
        }

        [Test]
        public void Stopwatch_ImplementsISyncVar()
        {
            var (netObj, sw) = CreateStopwatch();
            Assert.IsInstanceOf<ISyncVar>(sw);
            Cleanup(netObj);
        }

        [Test]
        public void Timer_WireSize_FiveBytes()
        {
            var (netObj, timer) = CreateTimer();
            timer.Start(10f);
            var writer = new NetWriter();
            timer.WriteTo(writer);
            Assert.AreEqual(5, writer.ToArray().Length); // 4 float + 1 byte
            Cleanup(netObj);
        }

        [Test]
        public void Stopwatch_WireSize_FiveBytes()
        {
            var (netObj, sw) = CreateStopwatch();
            sw.Start();
            sw.Tick(3f);
            var writer = new NetWriter();
            sw.WriteTo(writer);
            Assert.AreEqual(5, writer.ToArray().Length); // 4 float + 1 byte
            Cleanup(netObj);
        }

        #endregion

        #region Factory_Methods

        [Test]
        public void NetworkObject_SyncTimer_Factory()
        {
            var go = new GameObject("TestObj");
            var netObj = go.AddComponent<NetworkObject>();
            var timer = netObj.SyncTimer(15f);
            Assert.IsNotNull(timer);
            Assert.AreEqual(15f, timer.Remaining, 0.0001f);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void NetworkObject_SyncStopwatch_Factory()
        {
            var go = new GameObject("TestObj");
            var netObj = go.AddComponent<NetworkObject>();
            var sw = netObj.SyncStopwatch();
            Assert.IsNotNull(sw);
            Assert.AreEqual(0f, sw.Elapsed, 0.0001f);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Timer_And_Stopwatch_CoexistOnSameObject()
        {
            var go = new GameObject("TestObj");
            var netObj = go.AddComponent<NetworkObject>();
            var timer = netObj.SyncTimer(10f);
            var sw = netObj.SyncStopwatch();
            var syncVar = netObj.Sync(42);

            Assert.IsNotNull(timer);
            Assert.IsNotNull(sw);
            Assert.IsNotNull(syncVar);
            Assert.AreEqual(10f, timer.Remaining, 0.0001f);
            Assert.AreEqual(0f, sw.Elapsed, 0.0001f);
            Assert.AreEqual(42, syncVar.Value);

            Object.DestroyImmediate(go);
        }

        #endregion
    }
}
