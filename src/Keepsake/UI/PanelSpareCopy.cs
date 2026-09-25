using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace Keepsake.UI
{
    /// <summary>
    /// The question whether to keep a spare copy outside the profile, for profiles of Thunderstore
    /// Mod Manager and r2modman, whose Update existing profile replaces the profile folder. It is
    /// a box of its own over the panel, asked once the profile keeps something, and again from
    /// the Spare copy button at the top. See SpareCopy.
    /// </summary>
    public static partial class KeepsakePanel
    {
        private const float ModalWidth = 640f;
        private const float ModalInner = ModalWidth - 56f;

        private static GameObject _modal;
        private static GameObject _spareButton;

        /// <summary>Puts the question up when it is due and nothing else is up.</summary>
        private static void AskAboutSpareCopyIfDue()
        {
            if (_root == null || _modal != null || !SpareCopy.Asks) return;
            ShowSpareCopyModal();
        }

        private static void ShowSpareCopyModal() => ShowSpareCopyModal(false);

        /// <summary>
        /// A shade over the whole panel that takes every click, so nothing under it can be used, and
        /// in its middle a box with the question and the answers. Closing the panel leaves the
        /// question unanswered, and it is asked again the next time.
        /// </summary>
        /// <param name="sayingNo">
        /// The second step after No thanks: going without a spare copy is going without what
        /// Keepsake is for on this profile, so it is said plainly once more before it counts.
        /// </param>
        private static void ShowSpareCopyModal(bool sayingNo)
        {
            CloseModal();
            KeyCapture.Cancel();

            var shade = new GameObject("spare copy", typeof(RectTransform), typeof(Image));
            shade.transform.SetParent(_root.transform, false);
            var rect = (RectTransform)shade.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            shade.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            shade.transform.SetAsLastSibling();
            _modal = shade;

            var box = GUIManager.Instance.CreateWoodpanel(shade.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, ModalWidth, 300f, false);
            var layout = box.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 26, 26);
            layout.spacing = 10f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = box.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var wanted = SpareCopy.Wanted;
            if (sayingNo)
            {
                Wrapped("Go without a spare copy?", box.transform, ModalInner, 21, GUIManager.Instance.ValheimOrange, true);
                Wrapped("Without one, Update existing profile takes every setting and file you keep in this profile with it, " +
                        "and Keepsake has nothing to bring back. Installing or updating a modpack into the profile is still " +
                        "covered, since that leaves the profile folder in place.", box.transform, ModalInner, 15, Color.white);
                if (wanted == true)
                    Wrapped("The spare copy kept now is removed.", box.transform, ModalInner, 15, Kept);

                var confirm = ModalButtons(box.transform);
                FixedButton("Keep a spare copy after all", confirm, 270f, 34f, () => AnswerSpareCopy(true));
                FixedButton("No spare copy", confirm, 170f, 34f, () => AnswerSpareCopy(false));
                return;
            }

            Wrapped("A spare copy outside this profile", box.transform, ModalInner, 21, GUIManager.Instance.ValheimOrange, true);
            Wrapped("Update existing profile in Thunderstore Mod Manager and r2modman replaces this whole profile folder, " +
                    "and what you kept here goes with it. Keepsake can keep a spare copy of it outside the profile, " +
                    "and bring it back at the first launch after such an update.", box.transform, ModalInner, 15, Color.white);
            Wrapped("The copy holds only Keepsake's own files: the settings and files you keep, and their earlier versions. " +
                    "It goes in the mod manager's folder for the game, beside its profiles:", box.transform, ModalInner, 15, Color.white);
            Wrapped(SpareCopy.Folder ?? "", box.transform, ModalInner, 13, Dim);
            if (wanted != null)
                Wrapped(wanted.Value ? "A spare copy is kept now. No thanks removes it." : "No spare copy is kept now.",
                    box.transform, ModalInner, 15, Kept);

            var row = ModalButtons(box.transform);
            FixedButton("Keep a spare copy", row, 210f, 34f, () => AnswerSpareCopy(true));
            FixedButton("No thanks", row, 150f, 34f, () =>
            {
                // Saying no to what is not there yet changes nothing that could be lost.
                if (wanted == false) CloseModal();
                else ShowSpareCopyModal(true);
            });
            if (wanted != null) FixedButton("Leave it", row, 130f, 34f, CloseModal);
        }

        /// <summary>The row of answers at the foot of the box, a little apart from the text.</summary>
        private static Transform ModalButtons(Transform box)
        {
            var spacer = new GameObject("spacer", typeof(RectTransform));
            spacer.transform.SetParent(box, false);
            Fix(spacer, ModalInner, 4f);

            var row = new GameObject("buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(box, false);
            Fix(row, ModalInner, 34f);
            var buttons = row.GetComponent<HorizontalLayoutGroup>();
            buttons.childControlHeight = true;
            buttons.childControlWidth = false;
            buttons.childForceExpandWidth = false;
            buttons.childAlignment = TextAnchor.MiddleLeft;
            buttons.spacing = 10f;
            return row.transform;
        }

        private static void AnswerSpareCopy(bool keep)
        {
            var problem = SpareCopy.Answer(keep);
            CloseModal();
            UpdateSpareButton();

            if (problem != null)
            {
                Say(problem, true);
                return;
            }

            Sfx.Play(keep ? Sfx.Kept : Sfx.Released);
            Say(keep
                ? "A spare copy is kept beside the profiles, brought up to date as you play."
                : "No spare copy is kept. The Spare copy button at the top changes that.");
        }

        private static void CloseModal()
        {
            if (_modal != null) Object.Destroy(_modal);
            _modal = null;
        }

        /// <summary>The Spare copy button at the top, shown only for a profile that can have one, saying whether it has.</summary>
        private static void BuildSpareButton()
        {
            _spareButton = Button("Spare copy", _root.transform, SpareButtonWidth, 32f, ShowSpareCopyModal);
            UpdateSpareButton();
        }

        private const float SpareButtonWidth = 170f;

        private static void UpdateSpareButton()
        {
            if (_spareButton == null) return;

            _spareButton.SetActive(SpareCopy.Offered);
            var label = _spareButton.GetComponentInChildren<Text>();
            var wanted = SpareCopy.Wanted;
            if (label != null) label.text = wanted == true ? "Spare copy: on" : wanted == false ? "Spare copy: off" : "Spare copy";
            PlaceTopButtons();
        }
    }
}
