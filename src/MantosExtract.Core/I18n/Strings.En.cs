using System.Collections.Generic;

namespace MantosExtract.Core.I18n
{
    internal static partial class Strings
    {
        public static Dictionary<string, string> En() => new Dictionary<string, string>
        {
            ["me.app.title"] = "Mantos Extract",

            ["me.login.email.placeholder"] = "Email",
            ["me.login.password.placeholder"] = "Password",
            ["me.login.password.show"] = "Show",
            ["me.login.password.hide"] = "Hide",
            ["me.login.submit"] = "Sign in",
            ["me.login.submitting"] = "Signing in...",
            ["me.login.forgot"] = "Forgot my password",
            ["me.login.error.generic"] = "Could not sign in. Please try again.",
            ["me.login.error.emailRequired"] = "Enter your email.",
            ["me.login.error.passwordRequired"] = "Enter your password.",

            ["me.home.credits.label"] = "{0} credits",
            ["me.home.credits.one"] = "1 credit",
            ["me.home.credits.zero"] = "No credits",
            ["me.home.credits.unit"] = "cred.",
            ["me.home.settings.button"] = "Settings",
            ["me.home.empty.title"] = "Coming soon",
            ["me.home.empty.body"] = "Element extraction ships in the next stage of this project.",
            ["me.home.warning.dismiss"] = "Got it",

            ["me.settings.title"] = "Settings",
            ["me.settings.back"] = "Back",
            ["me.settings.language.label"] = "Language",
            ["me.settings.openai.label"] = "OpenAI key",
            ["me.settings.openai.placeholder"] = "sk-...",
            ["me.settings.openai.help"] = "Your shop uses its own OpenAI key to detect and extract images. Paste it here once.",
            ["me.settings.openai.save"] = "Save key",
            ["me.settings.openai.saved"] = "Key saved.",
            ["me.settings.openai.empty"] = "No key saved yet.",
            ["me.settings.logout"] = "Sign out",
            ["me.settings.logout.confirm"] = "Sign out of your account on this computer?",

            ["me.common.retry"] = "Try again",
            ["me.common.loading"] = "Loading...",
            ["me.common.error.unknown"] = "Something went wrong. Please try again.",
            ["me.common.details"] = "see technical details",
            ["me.table.element"] = "Element",
            ["me.table.status"] = "Status",

            ["me.detect.button"] = "Detect",
            ["me.detect.emptyState.title"] = "Select an image",
            ["me.detect.emptyState.body"] = "Click an image in your document, then click here.",
            ["me.detect.ready.title"] = "Image selected",
            ["me.detect.error.noKey"] = "Set your OpenAI key in Settings before detecting.",
            ["me.detect.error.noSelection"] = "Select an image in the document before detecting.",
            ["me.detect.error.exportFailed"] = "Could not export the selected image from CorelDRAW.",
            ["me.detect.progress.exporting"] = "Exporting image...",
            ["me.detect.progress.detecting"] = "Detecting elements...",

            ["me.select.title"] = "Detected elements",
            ["me.select.none"] = "No elements detected in this image.",
            ["me.select.extractButton"] = "Extract ({0})",
            ["me.select.selectedCount"] = "{0} selected",
            ["me.select.creditNote"] = "1 credit already used on detection. Extracting will cost {0} more.",
            ["me.select.backButton"] = "Select another image",

            ["me.extract.progress.extracting"] = "extracting...",
            ["me.extract.progress.upscaling"] = "upscaling...",
            ["me.extract.progress.queued"] = "queued",
            ["me.extract.progress.done"] = "done",
            ["me.extract.progress.failed"] = "failed",
            ["me.extract.title"] = "Extracting elements",
            ["me.extract.result.title"] = "Extracted elements",
            ["me.extract.result.summary"] = "{0} of {1} elements extracted successfully",
            ["me.extract.result.newImage"] = "Extract another image",
            ["me.extract.result.skippedNoCredits"] = "{0} element(s) were not extracted — out of credits.",

            ["me.error.network.title"] = "We couldn't connect",
            ["me.error.network.body"] = "Check your internet connection and try again.",

            ["me.credits.zero.title"] = "You're out of credits",
            ["me.credits.zero.body"] = "Talk to your shop to unlock more credits.",
            ["me.credits.zero.contact"] = "Ask whoever manages your shop's account to renew the plan on the mantosfc panel.",
        };
    }
}
