using System;
using System.Linq;
using BepInEx.Configuration;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace Keepsake.UI
{
    /// <summary>
    /// The right column: everything about the selected setting, and the one place where it is
    /// kept, released and given your value.
    /// </summary>
    public static partial class KeepsakePanel
    {
        private static float DetailInner => DetailWidth - 40f;

        /// <summary>Choice lists up to this long are shown as buttons, one per value; longer ones as a dropdown.</summary>
        private const int MaxChoiceButtons = 4;

        private static void ShowDetail()
        {
            if (_detail == null) return;
            Clear(_detail);

            var setting = SettingIndex.Find(_selectedId);
            var pin = _selectedId != null ? Keeper.Find(_selectedId) : null;

            if (setting == null && pin == null)
            {
                Wrapped("Select a setting to see what it does and to keep it.", _detail, DetailInner, 15, Dim);
                return;
            }

            if (setting == null)
            {
                UnloadedDetail(pin);
                return;
            }

            Wrapped(setting.ModName + "  /  " + setting.Section, _detail, DetailInner, 14, Dim);
            Wrapped(setting.Key, _detail, DetailInner, 21, pin != null ? Kept : GUIManager.Instance.ValheimOrange, true);

            var description = setting.Description.Trim();
            Wrapped(description.Length > 0 ? description : "The mod gives no description.", _detail, DetailInner, 15,
                description.Length > 0 ? Color.white : Dim);

            Spacer(6f);
            Facts(setting);
            Spacer(10f);

            if (pin == null) UnkeptControls(setting);
            else KeptControls(setting, pin);
        }

        /// <summary>The setting's default, what it accepts, and what may override it.</summary>
        private static void Facts(Setting setting)
        {
            if (setting.Default != null) Fact("Default", setting.Default);

            var range = RangeOf(setting.Entry);
            if (range != null) Fact("Allowed", range);

            var choices = ChoicesOf(setting.Entry);
            if (choices != null && choices.Length > MaxChoiceButtons)
                Fact("Choices", choices.Length <= 20 ? string.Join(", ", choices) : $"one of {choices.Length} values");

            var initial = Session.InitialOf(setting.Id);
            if (Session.IsChanged(setting) && initial != null) Fact("At launch", initial);

            if (setting.Server != ServerControl.None)
                Wrapped("While you are on a server that runs this mod, the server's value is used. Yours applies " +
                        "everywhere else and is back when you leave.", _detail, DetailInner, 13, Dim);
        }

        private static void Fact(string name, string value) =>
            Wrapped($"<color=#ffffff88>{name}:</color>  {value}", _detail, DetailInner, 15, Color.white);

        private static void UnkeptControls(Setting setting)
        {
            Wrapped("Value now", _detail, DetailInner, 13, Dim);
            Wrapped(setting.Current ?? "", _detail, DetailInner, 19, Color.white, true);
            Spacer(6f);

            var row = ButtonRow();
            FixedButton("Keep", row, 150f, 34f, () => Act(() => Keeper.Pin(setting), Sfx.Kept,
                () => $"{setting.Key} is kept at {setting.Current}. A profile sync leaves it alone now."));

            Wrapped("Keeping a setting holds it at its value through profile syncs. Once kept, its value " +
                    "can be changed here or in any config manager.", _detail, DetailInner, 13, Dim);
        }

        private static void KeptControls(Setting setting, Pin pin)
        {
            Wrapped("Your value", _detail, DetailInner, 13, Kept);
            ValueEditor(setting);
            Spacer(4f);

            if (pin.Profile != null)
                Fact("Profile's value", pin.Profile + (pin.Profile == pin.Value ? "  (the same)" : ""));

            Spacer(6f);
            var row = ButtonRow();
            FixedButton("Release", row, 150f, 34f, () => Act(() =>
            {
                Keeper.Unpin(pin.Id);
                return null;
            }, Sfx.Released, () => $"{setting.Key} follows the profile again" + (pin.Profile != null ? $", at {pin.Profile}." : ".")));

            if (setting.Default != null && setting.Current != setting.Default)
                FixedButton("Use default", row, 150f, 34f, () => Set(setting, setting.Default));

            Wrapped("Release hands the setting back to the profile and puts the profile's value back.",
                _detail, DetailInner, 13, Dim);
        }

        /// <summary>A kept setting whose mod has not bound it this session, or is not installed.</summary>
        private static void UnloadedDetail(Pin pin)
        {
            Wrapped(pin.File + "  /  " + pin.Section, _detail, DetailInner, 14, Dim);
            Wrapped(pin.Key, _detail, DetailInner, 21, Kept, true);
            Wrapped("This setting is not loaded right now: its mod is not installed, or has not read its settings " +
                    "yet. Your value goes back into its cfg file at every launch.", _detail, DetailInner, 15, Color.white);
            Spacer(6f);
            Fact("Your value", pin.Value);
            if (pin.Profile != null) Fact("Profile's value", pin.Profile);
            Spacer(6f);

            var row = ButtonRow();
            FixedButton("Release", row, 150f, 34f, () => Act(() =>
            {
                Keeper.Unpin(pin.Id);
                _selectedId = null;
                return null;
            }, Sfx.Released, () => $"{pin.Key} is no longer kept."));
        }

        // ---------- editing ----------

        private static void ValueEditor(Setting setting)
        {
            var type = setting.Entry.SettingType;
            var current = setting.Current ?? "";

            if (type == typeof(bool))
            {
                var on = string.Equals(current, "true", StringComparison.OrdinalIgnoreCase);
                var row = ButtonRow();
                ChoiceButton("On", on, row, () => Set(setting, "true"));
                ChoiceButton("Off", !on, row, () => Set(setting, "false"));
                return;
            }

            var choices = ChoicesOf(setting.Entry);
            if (choices != null && choices.Length > 1)
            {
                if (choices.Length <= MaxChoiceButtons)
                {
                    foreach (var choice in choices)
                    {
                        var value = choice;
                        var row = ButtonRow(30f);
                        ChoiceButton(value, value == current, row, () => Set(setting, value), DetailInner, 30f);
                    }
                    return;
                }

                ChoiceDropdown(setting, choices, current);
                return;
            }

            var fieldObject = GUIManager.Instance.CreateInputField(_detail,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero,
                InputField.ContentType.Standard, "", 16, DetailInner, 34f);
            Fix(fieldObject, DetailInner, 34f);

            var field = fieldObject.GetComponent<InputField>();
            field.text = current;
            field.onEndEdit.AddListener(text =>
            {
                if (text.Trim() == (setting.Current ?? "")) return;
                Set(setting, text);
            });

            Wrapped("Type a value and press Enter.", _detail, DetailInner, 12, Dim);
        }

        /// <summary>A scrolling list for choices too many for a button each, such as a key.</summary>
        private static void ChoiceDropdown(Setting setting, string[] choices, string current)
        {
            var options = choices.ToList();

            // A value outside the list, such as one edited into the cfg file by hand, is shown at
            // the top rather than letting the list claim a value the setting does not have.
            var index = options.IndexOf(current);
            if (index < 0)
            {
                options.Insert(0, current);
                index = 0;
            }

            var go = GUIManager.Instance.CreateDropDown(_detail, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, 15, DetailInner, 34f);
            Fix(go, DetailInner, 34f);

            var dropdown = go.GetComponent<Dropdown>();

            // The list is cloned from the template each time it opens, so the wheel speed is set
            // on the template's scroll rect; the list itself does not exist yet.
            var template = dropdown.template;
            var scroll = template != null ? template.GetComponentInChildren<ScrollRect>(true) : null;
            if (scroll != null) scroll.scrollSensitivity = 300f;

            dropdown.ClearOptions();
            dropdown.AddOptions(options);
            dropdown.SetValueWithoutNotify(index);
            dropdown.onValueChanged.AddListener(i =>
            {
                if (i < 0 || i >= options.Count || options[i] == setting.Current) return;
                Set(setting, options[i]);
            });
        }

        private static void ChoiceButton(string text, bool current, Transform row, Action onClick, float width = 110f, float height = 34f)
        {
            var go = Button(text, row, width, height, () => onClick());
            Fix(go, width, height);
            if (!current) return;

            // The value it has now: nothing to click, marked in the colour of a kept value.
            go.GetComponent<Button>().interactable = false;
            var label = go.GetComponentInChildren<Text>();
            if (label != null) label.color = Kept;
        }

        private static void Set(Setting setting, string value) =>
            Act(() => Keeper.SetValue(setting, value), Sfx.ValueSet, () => $"{setting.Key} is kept at {setting.Current}.");

        /// <summary>
        /// Runs an action from the detail column, redraws, then says what happened or why not. The
        /// sound plays only when it worked. The message is worked out afterwards, so it can name
        /// the value the action left behind.
        /// </summary>
        private static void Act(Func<string> action, string sound, Func<string> done)
        {
            string problem;
            try
            {
                problem = action();
            }
            catch (Exception ex)
            {
                Plugin.WarnOnce($"Keepsake: {ex.Message}", ex);
                problem = "That did not work, see the log.";
            }

            Populate(keepScroll: true);
            if (problem != null)
            {
                Say(problem, true);
                return;
            }

            Sfx.Play(sound);
            Say(done());
        }

        /// <summary>
        /// The values a setting can take, when there is a list of them: a plain enum, or an
        /// AcceptableValueList. Flag enums combine values and are typed instead.
        ///
        /// An enum is listed by value, each under the name the cfg file saves it as. Some enums
        /// give one value several names (KeyCode calls one key RightMeta, RightCommand and
        /// RightApple), and listing the names would offer choices that save as another one.
        /// </summary>
        private static string[] ChoicesOf(ConfigEntryBase entry)
        {
            var type = entry.SettingType;
            try
            {
                if (type.IsEnum && !type.IsDefined(typeof(FlagsAttribute), false))
                    return Enum.GetValues(type).Cast<object>()
                        .Select(v => TomlTypeConverter.ConvertToString(v, type))
                        .Distinct()
                        .ToArray();

                var acceptable = entry.Description?.AcceptableValues;
                if (acceptable == null) return null;

                var list = acceptable.GetType().GetProperty("AcceptableValues")?.GetValue(acceptable, null) as Array;
                if (list == null) return null;

                return list.Cast<object>().Select(v => TomlTypeConverter.ConvertToString(v, type)).ToArray();
            }
            catch (Exception ex)
            {
                Plugin.WarnOnce($"Keepsake: could not list the values of {entry.Definition.Key}: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>"0 to 100" for a setting with an AcceptableValueRange, otherwise null.</summary>
        private static string RangeOf(ConfigEntryBase entry)
        {
            var acceptable = entry.Description?.AcceptableValues;
            if (acceptable == null) return null;

            var type = acceptable.GetType();
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(AcceptableValueRange<>)) return null;

            var min = type.GetProperty("MinValue")?.GetValue(acceptable, null);
            var max = type.GetProperty("MaxValue")?.GetValue(acceptable, null);
            return min != null && max != null ? $"{min} to {max}" : null;
        }
    }
}
