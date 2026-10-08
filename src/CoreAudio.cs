using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace VolumeKeeper
{
    // COMメソッドはすべて [PreserveSig] でHRESULTをそのまま受け取る（失敗は hr < 0、成功には S_OK 以外の正の値もある）
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey { public Guid fmtid; public int pid; }

    [StructLayout(LayoutKind.Sequential)]
    struct PropVariant { public ushort vt; public ushort r1, r2, r3; public IntPtr p; public IntPtr p2; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out int count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDB, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDB);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(int channel, float levelDB, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(int channel, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(int channel, out float levelDB);
        [PreserveSig] int GetChannelVolumeLevelScalar(int channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr guid, int flags, out IntPtr ctl);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr guid, int flags, out IntPtr vol);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr a, IntPtr b);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr a, IntPtr b);
        [PreserveSig] int GetGroupingParam(out Guid g);
        [PreserveSig] int SetGroupingParam(IntPtr a, IntPtr b);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr n);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out int pid);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    public class Endpoint
    {
        public string Id;
        public string Name;
        public int State;
        public float Volume;
    }

    // 再生デバイス全体の音量（マスター音量）
    public class EndpointControl
    {
        internal IAudioEndpointVolume Control;
        public string Name;

        public float Volume
        {
            get { float v; Marshal.ThrowExceptionForHR(Control.GetMasterVolumeLevelScalar(out v)); return v; }
            set { Guid ctx = Guid.Empty; Marshal.ThrowExceptionForHR(Control.SetMasterVolumeLevelScalar(value, ref ctx)); }
        }

        public bool Muted
        {
            get { bool m; Marshal.ThrowExceptionForHR(Control.GetMute(out m)); return m; }
            set { Guid ctx = Guid.Empty; Marshal.ThrowExceptionForHR(Control.SetMute(value, ref ctx)); }
        }
    }

    // アプリ別の音量セッション
    public class Session
    {
        internal ISimpleAudioVolume Control;
        public string EndpointName;
        public string InstanceId;
        public int ProcessId;
        public string ProcessName;
        public string ExePath;
        public int State;
        public float Volume;
        public bool Muted;

        // 「System」はシステム音（PID 0）を表す
        public string Key { get { return ProcessId == 0 ? "System" : ProcessName; } }

        public bool SetVolume(float level)
        {
            Guid ctx = Guid.Empty;
            if (Control.SetMasterVolume(level, ref ctx) < 0) return false;
            Volume = level;
            return true;
        }

        public bool SetMute(bool mute)
        {
            Guid ctx = Guid.Empty;
            if (Control.SetMute(mute, ref ctx) < 0) return false;
            Muted = mute;
            return true;
        }
    }

    // Windows Core Audio APIで再生デバイスとアプリ別セッションの音量を読み書きする
    public static class Audio
    {
        const int eRender = 0;
        const int DEVICE_STATE_ACTIVE = 1;
        const int DEVICE_STATEMASK_ALL = 0xF;
        const int AudioSessionStateExpired = 2;
        const string EndpointVolumeIid = "5CDF2C82-841E-4546-9722-0CF74078229A";
        const string SessionManagerIid = "77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F";
        static PropertyKey FriendlyNameKey = new PropertyKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PropVariant pvar);

        static string FriendlyName(IMMDevice dev)
        {
            IPropertyStore store;
            if (dev.OpenPropertyStore(0 /* STGM_READ */, out store) < 0) return "";
            var v = new PropVariant();
            try
            {
                if (store.GetValue(ref FriendlyNameKey, out v) < 0 || v.vt != 31 /* VT_LPWSTR */) return "";
                return Marshal.PtrToStringUni(v.p);
            }
            finally
            {
                // GetValue が確保した文字列を解放する
                PropVariantClear(ref v);
            }
        }

        static T Activate<T>(IMMDevice dev, string iid)
        {
            Guid g = new Guid(iid);
            object o;
            Marshal.ThrowExceptionForHR(dev.Activate(ref g, 23, IntPtr.Zero, out o));
            return (T)o;
        }

        static List<IMMDevice> RenderDevices(int stateMask)
        {
            var list = new List<IMMDevice>();
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            IMMDeviceCollection col;
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(eRender, stateMask, out col));
            int n;
            col.GetCount(out n);
            for (int i = 0; i < n; i++)
            {
                IMMDevice d;
                if (col.Item(i, out d) >= 0) list.Add(d);
            }
            return list;
        }

        // 操作対象の再生デバイスを1台だけ決める。見つからなければ null。
        // pattern が空なら既定の再生デバイス。指定があれば名前が完全一致するものを優先し、なければ名前に pattern を含む最初の1台。
        // マスター音量・アプリ別音量・自動復元がすべて同じ1台を見るよう、複数一致でも1台に絞る
        static IMMDevice TargetDevice(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                IMMDevice d;
                return enumerator.GetDefaultAudioEndpoint(eRender, 0 /* eConsole */, out d) >= 0 ? d : null;
            }
            IMMDevice partial = null;
            foreach (var d in RenderDevices(DEVICE_STATE_ACTIVE))
            {
                string name = FriendlyName(d);
                if (string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase)) return d;
                if (partial == null && name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0) partial = d;
            }
            return partial;
        }

        public static List<Endpoint> GetEndpoints()
        {
            var result = new List<Endpoint>();
            foreach (var d in RenderDevices(DEVICE_STATEMASK_ALL))
            {
                var e = new Endpoint();
                d.GetId(out e.Id);
                d.GetState(out e.State);
                try { e.Name = FriendlyName(d); } catch (COMException) { e.Name = ""; }
                e.Volume = -1;
                if (e.State == DEVICE_STATE_ACTIVE)
                {
                    try
                    {
                        var vol = Activate<IAudioEndpointVolume>(d, EndpointVolumeIid);
                        vol.GetMasterVolumeLevelScalar(out e.Volume);
                    }
                    catch (COMException) { }
                }
                result.Add(e);
            }
            return result;
        }

        // 対象の再生デバイスのマスター音量。見つからなければ null
        public static EndpointControl FindEndpoint(string pattern)
        {
            var d = TargetDevice(pattern);
            if (d == null) return null;
            try { return new EndpointControl { Name = FriendlyName(d), Control = Activate<IAudioEndpointVolume>(d, EndpointVolumeIid) }; }
            catch (COMException) { return null; }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(int access, bool inherit, int pid);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, StringBuilder name, ref int size);

        // Process.GetProcessById は呼ぶたびに全プロセスを列挙して重いので、実行ファイルのパスだけ引いてキャッシュする。
        // キーはセッションのインスタンスID（PIDと実行ファイルを含む）なので、PIDが別プロセスに再利用されても取り違えない
        static Dictionary<string, string> pathCache = new Dictionary<string, string>();

        static string ProcessPath(string instanceId, int pid)
        {
            if (pid == 0) return "";
            string path;
            if (pathCache.TryGetValue(instanceId, out path)) return path;
            path = "";
            IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (h != IntPtr.Zero)
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (QueryFullProcessImageNameW(h, 0, sb, ref size)) path = sb.ToString();
                CloseHandle(h);
            }
            // 取得に失敗した場合はキャッシュせず、次回もう一度試す
            if (path.Length > 0) pathCache[instanceId] = path;
            return path;
        }

        // 対象の再生デバイス上の、期限切れでないセッションを返す（endpointPattern が空なら既定の再生デバイス）
        public static List<Session> GetSessions(string endpointPattern)
        {
            var result = new List<Session>();
            var d = TargetDevice(endpointPattern);
            if (d == null) return result;
            string devName = FriendlyName(d);
            IAudioSessionManager2 mgr;
            try { mgr = Activate<IAudioSessionManager2>(d, SessionManagerIid); } catch (COMException) { return result; }
            IAudioSessionEnumerator en;
            if (mgr.GetSessionEnumerator(out en) < 0) return result;
            int n;
            if (en.GetCount(out n) < 0) return result;
            for (int i = 0; i < n; i++)
            {
                IAudioSessionControl2 s;
                if (en.GetSession(i, out s) < 0) continue;
                var info = new Session { EndpointName = devName };
                if (s.GetState(out info.State) < 0 || info.State == AudioSessionStateExpired) continue;
                if (s.GetSessionInstanceIdentifier(out info.InstanceId) < 0 || info.InstanceId == null) continue;
                // 複数プロセスのセッションは AUDCLNT_S_NO_SINGLE_PROCESS（成功扱いの正の値）を返す
                if (s.GetProcessId(out info.ProcessId) < 0) continue;
                info.ExePath = ProcessPath(info.InstanceId, info.ProcessId);
                info.ProcessName = info.ProcessId == 0 ? "System" : System.IO.Path.GetFileNameWithoutExtension(info.ExePath);
                info.Control = (ISimpleAudioVolume)s;
                if (info.Control.GetMasterVolume(out info.Volume) < 0) continue;
                info.Control.GetMute(out info.Muted);
                result.Add(info);
            }
            // 見えなくなったセッションのキャッシュは捨てる
            var alive = new HashSet<string>();
            foreach (var s in result) alive.Add(s.InstanceId);
            foreach (var id in new List<string>(pathCache.Keys))
                if (!alive.Contains(id)) pathCache.Remove(id);
            return result;
        }
    }
}
