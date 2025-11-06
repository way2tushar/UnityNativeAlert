// Assets/way2tushar/NativeAlerts/Runtime/NativeAlert.cs
// Java-free Android + iOS bridge (namespace: way2tushar.NativeAlerts)
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace way2tushar.NativeAlerts
{
    public enum AlertTheme { System, Light, Dark }
    public enum AlertButtonStyle { Default, Cancel, Destructive }

    [Serializable]
    public class AlertButton
    {
        public string text;
        public AlertButtonStyle style = AlertButtonStyle.Default;
    }

    [Serializable]
    public class AlertOptions
    {
        public string title;
        public string message;
        public List<AlertButton> buttons = new() { new AlertButton { text = "OK" } };
        public AlertTheme theme = AlertTheme.System;
    }

    public static class NativeAlert
    {
        static int _nextId = 1;
        static readonly Dictionary<int, TaskCompletionSource<int>> _pending = new();

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void _na_showAlert(string json, int id);
#endif

        public static Task<int> ShowAsync(AlertOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.buttons == null || options.buttons.Count == 0)
                options.buttons = new() { new AlertButton { text = "OK" } };

            var id = _nextId++;
            var tcs = new TaskCompletionSource<int>();
            _pending[id] = tcs;

            var json = JsonUtility.ToJson(options);

#if UNITY_EDITOR
            Debug.Log($"[NativeAlert] Editor stub: {json}");
            tcs.SetResult(0);
#elif UNITY_IOS
            _na_showAlert(json, id);
#elif UNITY_ANDROID
            ShowAndroid_NoGradle(json, id);
#else
            tcs.SetResult(0);
#endif
            return tcs.Task;
        }

        // Called from native iOS (UnitySendMessage payload "<id>|<index>")
        public static void Resolve(string payload)
        {
            var parts = payload.Split('|');
            if (parts.Length != 2) return;
            if (!int.TryParse(parts[0], out var id)) return;
            if (!int.TryParse(parts[1], out var index)) return;
            TryResolve(id, index);
        }

        static void TryResolve(int id, int index)
        {
            if (_pending.TryGetValue(id, out var tcs))
            {
                _pending.Remove(id);
                tcs.TrySetResult(index);
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        class OnClickProxy : AndroidJavaProxy
        {
            readonly int _id;
            readonly int _index;
            public OnClickProxy(int id, int index)
                : base("android.content.DialogInterface$OnClickListener")
            { _id = id; _index = index; }

            // void onClick(DialogInterface dialog, int which)
            void onClick(AndroidJavaObject dialog, int which)
            {
                TryResolve(_id, _index);
                try { dialog?.Call("dismiss"); } catch { }
            }
        }

        class OnItemClickProxy : AndroidJavaProxy
        {
            readonly int _id;
            public OnItemClickProxy(int id)
                : base("android.content.DialogInterface$OnClickListener")
            { _id = id; }

            void onClick(AndroidJavaObject dialog, int which)
            {
                TryResolve(_id, which);
                try { dialog?.Call("dismiss"); } catch { }
            }
        }

        static void ShowAndroid_NoGradle(string json, int id)
        {
            var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            if (activity == null) { TryResolve(id, 0); return; }

            activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                try
                {
                    var obj = new AndroidJavaObject("org.json.JSONObject", json);
                    string title = obj.Call<string>("optString", "title", "");
                    string message = obj.Call<string>("optString", "message", "");
                    string themeStr = obj.Call<string>("optString", "theme", "System");
                    var buttons = obj.Call<AndroidJavaObject>("optJSONArray", "buttons");
                    if (buttons == null)
                        buttons = new AndroidJavaObject("org.json.JSONArray");

                    // DeviceDefault dialog themes (framework)
                    int themeId = 0; // System default
                    if (themeStr == "Light")
                        themeId = GetStyleId("android.R$style", "Theme_DeviceDefault_Light_Dialog_Alert");
                    else if (themeStr == "Dark")
                        themeId = GetStyleId("android.R$style", "Theme_DeviceDefault_Dialog_Alert");

                    AndroidJavaObject ctx = activity;
                    if (themeId != 0)
                        ctx = new AndroidJavaObject("android.view.ContextThemeWrapper", activity, themeId);

                    var builder = new AndroidJavaObject("android.app.AlertDialog$Builder", ctx);
                    if (!string.IsNullOrEmpty(title)) builder.Call<AndroidJavaObject>("setTitle", title);
                    if (!string.IsNullOrEmpty(message)) builder.Call<AndroidJavaObject>("setMessage", message);
                    builder.Call<AndroidJavaObject>("setCancelable", false);

                    int count = buttons.Call<int>("length");
                    if (count == 0)
                    {
                        builder.Call<AndroidJavaObject>("setPositiveButton", "OK", new OnClickProxy(id, 0));
                    }
                    else if (count <= 3)
                    {
                        int cancelIdx = IndexOfStyle(buttons, "Cancel");
                        int destructiveIdx = FirstWithStyle(buttons, "Destructive", cancelIdx);
                        int defaultIdx = FirstDefault(buttons, cancelIdx, destructiveIdx);

                        if (destructiveIdx >= 0)
                            builder.Call<AndroidJavaObject>(
                                "setPositiveButton", BtnText(buttons, destructiveIdx), new OnClickProxy(id, destructiveIdx));
                        if (cancelIdx >= 0)
                            builder.Call<AndroidJavaObject>(
                                "setNegativeButton", BtnText(buttons, cancelIdx), new OnClickProxy(id, cancelIdx));
                        if (defaultIdx >= 0)
                            builder.Call<AndroidJavaObject>(
                                "setNeutralButton", BtnText(buttons, defaultIdx), new OnClickProxy(id, defaultIdx));
                    }
                    else
                    {
                        // >3: List of items (immediate resolve)
                        int n = count;
                        string[] managed = new string[n];
                        for (int i = 0; i < n; i++) managed[i] = BtnText(buttons, i);
                        using (var javaArray = ToJavaStringArray(managed))
                        {
                            builder.Call<AndroidJavaObject>("setItems", javaArray, new OnItemClickProxy(id));
                        }
                        int cancelIdx = IndexOfStyle(buttons, "Cancel");
                        if (cancelIdx >= 0)
                            builder.Call<AndroidJavaObject>("setNegativeButton", BtnText(buttons, cancelIdx), new OnClickProxy(id, cancelIdx));
                    }

                    var dialog = builder.Call<AndroidJavaObject>("create");
                    dialog.Call("setCanceledOnTouchOutside", false);
                    dialog.Call("show");
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    TryResolve(id, 0);
                }
            }));
        }

        static int GetStyleId(string className, string field)
        {
            try
            {
                using var styleClass = new AndroidJavaClass(className);
                return styleClass.GetStatic<int>(field);
            }
            catch { return 0; }
        }

        static string BtnText(AndroidJavaObject arr, int i)
        {
            var obj = arr.Call<AndroidJavaObject>("getJSONObject", i);
            return obj.Call<string>("optString", "text", "OK");
        }

        static string BtnStyle(AndroidJavaObject arr, int i)
        {
            var obj = arr.Call<AndroidJavaObject>("getJSONObject", i);
            return obj.Call<string>("optString", "style", "Default");
        }

        static int IndexOfStyle(AndroidJavaObject arr, string style)
        {
            int n = arr.Call<int>("length");
            for (int i = 0; i < n; i++) if (BtnStyle(arr, i) == style) return i;
            return -1;
        }

        static int FirstWithStyle(AndroidJavaObject arr, string style, int exclude)
        {
            int n = arr.Call<int>("length");
            for (int i = 0; i < n; i++) if (i != exclude && BtnStyle(arr, i) == style) return i;
            return -1;
        }

        static int FirstDefault(AndroidJavaObject arr, int a, int b)
        {
            int n = arr.Call<int>("length");
            for (int i = 0; i < n; i++)
                if (i != a && i != b && BtnStyle(arr, i) == "Default") return i;
            for (int i = 0; i < n; i++) if (i != a && i != b) return i;
            return -1;
        }

        static AndroidJavaObject ToJavaStringArray(string[] managed)
        {
            using var cls = new AndroidJavaClass("java.lang.reflect.Array");
            using var stringClass = new AndroidJavaClass("java.lang.String");
            var jArr = cls.CallStatic<AndroidJavaObject>("newInstance", stringClass, managed.Length);
            for (int i = 0; i < managed.Length; i++)
            {
                using var s = new AndroidJavaObject("java.lang.String", managed[i]);
                cls.CallStatic("set", jArr, i, s);
            }
            return jArr;
        }
#endif
    }
}
