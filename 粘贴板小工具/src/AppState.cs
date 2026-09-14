using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace QuietClip
{
    [DataContract]
    internal sealed class ClipEntry
    {
        [DataMember(Order = 1)]
        public string Id { get; set; }

        [DataMember(Order = 2)]
        public string Text { get; set; }

        [DataMember(Order = 3)]
        public long CapturedAtUtcTicks { get; set; }

        public DateTime CapturedAtLocal
        {
            get
            {
                try
                {
                    return new DateTime(CapturedAtUtcTicks, DateTimeKind.Utc).ToLocalTime();
                }
                catch
                {
                    return DateTime.Now;
                }
            }
        }

        public ClipEntry Clone()
        {
            return new ClipEntry
            {
                Id = Id,
                Text = Text,
                CapturedAtUtcTicks = CapturedAtUtcTicks
            };
        }
    }

    [DataContract]
    internal sealed class AppState
    {
        [DataMember(Order = 1)]
        public int MaxItems { get; set; }

        [DataMember(Order = 2)]
        public string HotkeyModifiers { get; set; }

        [DataMember(Order = 3)]
        public string HotkeyKey { get; set; }

        [DataMember(Order = 4)]
        public int WindowWidth { get; set; }

        [DataMember(Order = 5)]
        public int WindowHeight { get; set; }

        [DataMember(Order = 6)]
        public int WindowX { get; set; }

        [DataMember(Order = 7)]
        public int WindowY { get; set; }

        [DataMember(Order = 8)]
        public List<ClipEntry> Items { get; set; }

        [DataMember(Order = 9)]
        public string ThemeName { get; set; }

        public static AppState CreateDefault()
        {
            return new AppState
            {
                MaxItems = 10,
                HotkeyModifiers = "Ctrl + Shift",
                HotkeyKey = "V",
                WindowWidth = 440,
                WindowHeight = 640,
                WindowX = -1,
                WindowY = -1,
                Items = new List<ClipEntry>(),
                ThemeName = "银河紫"
            };
        }

        public void Normalize()
        {
            if (MaxItems < 1 || MaxItems > 200)
                MaxItems = 10;
            if (String.IsNullOrWhiteSpace(HotkeyModifiers))
                HotkeyModifiers = "Ctrl + Shift";
            if (String.IsNullOrWhiteSpace(HotkeyKey))
                HotkeyKey = "V";
            if (WindowWidth < 280 || WindowWidth > 1800)
                WindowWidth = 440;
            if (WindowHeight < 340 || WindowHeight > 1400)
                WindowHeight = 640;
            if (!Theme.IsChoice(ThemeName))
                ThemeName = "银河紫";
            if (Items == null)
                Items = new List<ClipEntry>();

            Items.RemoveAll(delegate(ClipEntry item)
            {
                return item == null || String.IsNullOrWhiteSpace(item.Text);
            });

            if (Items.Count > MaxItems)
                Items.RemoveRange(MaxItems, Items.Count - MaxItems);
        }
    }

    internal static class StateStore
    {
        private static readonly string DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuietClip");
        private static readonly string StatePath = Path.Combine(DataDirectory, "state.json");

        public static AppState Load()
        {
            if (!File.Exists(StatePath))
                return AppState.CreateDefault();

            try
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppState));
                using (FileStream stream = File.OpenRead(StatePath))
                {
                    AppState state = serializer.ReadObject(stream) as AppState;
                    if (state == null)
                        return AppState.CreateDefault();
                    state.Normalize();
                    return state;
                }
            }
            catch
            {
                return AppState.CreateDefault();
            }
        }

        public static void Save(AppState state)
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                string temporaryPath = StatePath + ".tmp";
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppState));
                using (FileStream stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    serializer.WriteObject(stream, state);
                }
                File.Copy(temporaryPath, StatePath, true);
                File.Delete(temporaryPath);
            }
            catch
            {
                // Clipboard capture must never be interrupted by a persistence failure.
            }
        }
    }
}
