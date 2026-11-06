using System;
using System.Windows.Forms;

namespace PBZKeyForge
{
    public partial class MainForm
    {
        void ShowKeyEnhancementHelp()
        {
            ShowFeedbackDialog(I18n.Str("enhancement.helpbody") + "\r\n\r\n"
                + I18n.Str("enhancement.helplimits") + "\r\n\r\n"
                + I18n.Str("live.overlayhelp") + "\r\n\r\n"
                + "PBZKeyForge: " + ProjectLinks.Project + "\r\nNexus Mods: " + ProjectLinks.Nexus
                + "\r\nKintaro UE4SS: " + ProjectLinks.Ue4ss + "\r\nUE4SS: " + ProjectLinks.Ue4ssUpstream
                + "\r\n\r\n" + I18n.Str("app.credits"), I18n.Str("enhancement.title") + " · " + I18n.Str("enhancement.help"));
        }

        void ShowApplicationAbout()
        {
            modalActive = true;
            try
            {
                using (var dialog = new ApplicationAboutDialog(delegate { bool result = !IsApplying && ConfirmDiscard(); modalActive = true; return result; }))
                {
                    dialog.ShowDialog(this);
                    if (dialog.RestartForUpdate) { updatingApplication = true; Close(); }
                }
            }
            finally { EndModal(); }
        }
        bool updatingApplication;
    }
}
