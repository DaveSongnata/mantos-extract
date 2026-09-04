using System.Collections.Generic;
using System.Text;

namespace MantosExtract.Core.I18n
{
    /// <summary>
    /// Builds the script that hands the string catalog to a WebView2 page — pure string work,
    /// testable on net8.0 with no browser/COM/Windows API involved. Reimplementation of
    /// Optimus.Core.I18n.I18nScript with the MantosExtract-prefixed globals (each addin owns its
    /// copy, no shared package between the sibling repos).
    ///
    /// Injected via AddScriptToExecuteOnDocumentCreatedAsync, which runs BEFORE the page's own
    /// script: the HTML carries no text of its own (every element is data-i18n), so a dictionary
    /// arriving as a normal message would show a flash of empty labels first.
    /// </summary>
    public static class I18nScript
    {
        public static string Build(LocalizationService localization)
        {
            Language language = localization.Current;
            var sb = new StringBuilder(32 * 1024);

            sb.Append("window.MANTOSEXTRACT_LANG=").Append(Quote(LocalizationService.TagOf(language))).Append(";");
            sb.Append("window.MANTOSEXTRACT_LANGS=[\"pt\",\"es\",\"en\"];");
            sb.Append("window.MANTOSEXTRACT_LANG_KEY=").Append(Quote(language.ToString().ToLowerInvariant())).Append(";");
            sb.Append("window.MANTOSEXTRACT_I18N=").Append(Json(localization.Dictionary())).Append(";");

            // Missing key -> the key itself, so a gap is visible/reportable, never a blank label.
            sb.Append(@"window.T=function(k){")
              .Append(@"var s=(window.MANTOSEXTRACT_I18N&&window.MANTOSEXTRACT_I18N[k])||k;")
              .Append(@"for(var i=1;i<arguments.length;i++){")
              .Append(@"s=s.split('{'+(i-1)+'}').join(String(arguments[i]));}")
              .Append(@"return s;};");

            sb.Append(@"window.applyI18n=function(root){")
              .Append(@"var scope=root||document;")
              .Append(@"var n=scope.querySelectorAll('[data-i18n]');")
              .Append(@"for(var i=0;i<n.length;i++){n[i].textContent=window.T(n[i].getAttribute('data-i18n'));}")
              .Append(@"var t=scope.querySelectorAll('[data-i18n-title]');")
              .Append(@"for(var j=0;j<t.length;j++){t[j].setAttribute('title',window.T(t[j].getAttribute('data-i18n-title')));}")
              .Append(@"var p=scope.querySelectorAll('[data-i18n-placeholder]');")
              .Append(@"for(var k=0;k<p.length;k++){p[k].setAttribute('placeholder',window.T(p[k].getAttribute('data-i18n-placeholder')));}")
              .Append(@"try{document.documentElement.setAttribute('lang',window.MANTOSEXTRACT_LANG);}catch(e){}};")
              .Append(@"document.addEventListener('DOMContentLoaded',function(){window.applyI18n();});");

            return sb.ToString();
        }

        public static string Json(IReadOnlyDictionary<string, string> dictionary)
        {
            var sb = new StringBuilder(24 * 1024);
            sb.Append('{');

            bool first = true;
            foreach (KeyValuePair<string, string> kv in dictionary)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Quote(kv.Key)).Append(':').Append(Quote(kv.Value));
            }

            return sb.Append('}').ToString();
        }

        /// <summary>JSON string literal. Escapes control chars plus &lt;/&gt;/&amp; — a literal
        /// &lt;/script&gt; inside a value would otherwise terminate the injected block (both a
        /// render bug and an injection vector).</summary>
        public static string Quote(string? value)
        {
            var sb = new StringBuilder((value?.Length ?? 0) + 16);
            sb.Append('"');

            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '<': sb.Append("\\u003c"); break;
                    case '>': sb.Append("\\u003e"); break;
                    case '&': sb.Append("\\u0026"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }

            return sb.Append('"').ToString();
        }
    }
}
