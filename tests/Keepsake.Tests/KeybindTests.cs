using System.IO;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
using Xunit;

namespace Keepsake.Tests
{
    /// <summary>
    /// Keybinds on Keepsake's side of the Bindrune link: left alone while Bindrune is loaded, and
    /// taken over from bindrune.keys only when asked while it is not. The files the two share are
    /// checked in ContractTests; this is what Keepsake does with them.
    /// </summary>
    [Collection(ProfileCollection.Name)]
    public class KeybindTests
    {
        private static string Line(string key, string value, string profile) =>
            $"a.cfg\tGeneral\t{key}\t{value}\t{profile}";

        private static (ConfigEntry<KeyCode> open, ConfigEntry<KeyboardShortcut> combo) Keys(TestProfile profile)
        {
            var config = profile.Mod("a.cfg");
            return (config.Bind("General", "Open", KeyCode.F1, "Opens it."),
                    config.Bind("General", "Combo", new KeyboardShortcut(KeyCode.G, KeyCode.LeftAlt), "Does it."));
        }

        // ---------- while Bindrune is loaded ----------

        [Fact]
        public void AKeybindIsNotKeptWhileBindruneIsLoaded()
        {
            using var profile = new TestProfile();
            Keys(profile);
            profile.Index();
            SettingIndex.IsBindruneLoaded = () => true;

            Assert.Equal(Keeper.BindruneKeepsKeys, Keeper.Pin(profile.Setting("a.cfg", "General", "Open")));
            Assert.False(Keeper.CanPin(profile.Setting("a.cfg", "General", "Open")));
            Assert.Empty(profile.PinLines());
        }

        [Fact]
        public void AWaitingKeybindIsNeitherPutBackNorFollowed()
        {
            using var profile = new TestProfile();
            profile.WritePins(Line("Open", "F7", "F1"));
            var (open, _) = Keys(profile);
            SettingIndex.IsBindruneLoaded = () => true;

            Keeper.Reconcile();
            Assert.Equal(KeyCode.F1, open.Value);

            // What Bindrune sets is its own choice.
            open.Value = KeyCode.F9;
            Keeper.Flush();
            Assert.Equal(new[] { Line("Open", "F7", "F1") }, profile.PinLines());
        }

        [Fact]
        public void ReleasingAWaitingKeybindOnlyLetsGoOfIt()
        {
            using var profile = new TestProfile();
            profile.WritePins(Line("Open", "F7", "F2"));
            var (open, _) = Keys(profile);
            profile.Index();
            SettingIndex.IsBindruneLoaded = () => true;

            Keeper.Unpin(PinFile.IdOf("a.cfg", "General", "Open"));

            Assert.Empty(profile.PinLines());
            Assert.Equal(KeyCode.F1, open.Value);
        }

        [Fact]
        public void NothingIsOfferedFromBindruneWhileItIsLoaded()
        {
            using var profile = new TestProfile();
            WriteBindruneKeys(profile, "cfg:isimp.a:General:Open\tF7\tF1\t1");
            Keys(profile);
            profile.Index();
            SettingIndex.IsBindruneLoaded = () => true;

            Assert.Empty(Keeper.BindruneKeys());
        }

        // ---------- without Bindrune ----------

        private static void WriteBindruneKeys(TestProfile profile, params string[] keys) =>
            File.WriteAllLines(BindruneLink.KeysFile, new[] { BindruneLink.StateVersion, "", "[keys]" }.Concat(keys));

        [Fact]
        public void TakingOverKeepsBindrunesKeysInUseAndLeavesItsFileAlone()
        {
            using var profile = new TestProfile();
            WriteBindruneKeys(profile,
                "cfg:isimp.a:General:Open\tF7\tF1\t1",
                "cfg:isimp.a:General:Combo\tH + LeftAlt\tG + LeftAlt\t1",
                "cfg:isimp.a:General:Aside\tF8\tF9\t0");
            var before = File.ReadAllText(BindruneLink.KeysFile);
            var (open, combo) = Keys(profile);
            profile.Index();

            Assert.Equal(2, Keeper.BindruneKeys().Count);
            Assert.Equal(2, Keeper.TakeOverFromBindrune());

            Assert.Equal(KeyCode.F7, open.Value);
            Assert.Equal(KeyCode.H, combo.Value.MainKey);
            Assert.Equal(new[] { Line("Combo", "H + LeftAlt", "G + LeftAlt"), Line("Open", "F7", "F1") }, profile.PinLines());
            Assert.Equal(before, File.ReadAllText(BindruneLink.KeysFile));
        }

        [Fact]
        public void AKeyKeptHereAlreadyIsNotOfferedAgain()
        {
            using var profile = new TestProfile();
            WriteBindruneKeys(profile, "cfg:isimp.a:General:Open\tF7\tF1\t1");
            Keys(profile);
            profile.Index();
            Keeper.Pin(profile.Setting("a.cfg", "General", "Open"));

            Assert.Empty(Keeper.BindruneKeys());
        }
    }
}
