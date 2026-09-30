// my-rekkr — English / Arabic strings for the touch UI, plus a small Arabic shaper.
// Unity IMGUI draws code points left-to-right with no shaping, so Arabic text is converted
// to Presentation Forms-B (contextual glyphs, lam-alef ligatures) and reordered for RTL.
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Collections.Generic;
using System.Text;

namespace ManagedDoom.UnityPort
{
    public static class Loc
    {
        public static bool Arabic;

        private static readonly Dictionary<string, (string en, string ar)> table = new Dictionary<string, (string, string)>
        {
            ["settings"] = ("SETTINGS", "الإعدادات"),
            ["tab_controls"] = ("CONTROLS", "التحكم"),
            ["tab_motion"] = ("GYRO & VIBRATION", "الجيروسكوب والاهتزاز"),
            ["tab_display"] = ("DISPLAY & SOUND", "العرض والصوت"),
            ["tab_graphics"] = ("GRAPHICS", "الرسوميات"),
            ["preset"] = ("Graphics preset", "نمط الرسوميات"),
            ["preset_0"] = ("CLASSIC (original)", "كلاسيكي (الأصلي)"),
            ["preset_1"] = ("BALANCED", "متوازن"),
            ["preset_2"] = ("ENHANCED", "محسّن"),
            ["preset_3"] = ("MASTERPIECE", "تحفة"),
            ["preset_4"] = ("CUSTOM", "مخصص"),
            ["grade_3"] = ("VOXILE (FILMIC)", "سينمائي VOXILE"),
            ["gfx_world"] = ("World effects", "مؤثرات العالم"),
            ["fx_sky"] = ("Living sky + sun", "سماء حية + شمس"),
            ["fx_water"] = ("Real water + lava", "مياه حقيقية + حمم"),
            ["fx_weather"] = ("Weather", "الطقس"),
            ["weather_0"] = ("AUTO", "تلقائي"),
            ["weather_1"] = ("RAIN", "مطر"),
            ["weather_2"] = ("SNOW", "ثلج"),
            ["weather_3"] = ("OFF", "إيقاف"),
            ["fx_fog"] = ("Atmospheric fog", "ضباب جوي"),
            ["fx_lights"] = ("Dynamic lights", "إضاءة ديناميكية"),
            ["fx_rays"] = ("Sun rays", "أشعة الشمس"),
            ["fx_ao"] = ("Soft shadows (AO)", "ظلال ناعمة (AO)"),
            ["fx_particles"] = ("Particles", "جسيمات"),
            ["fx_dof"] = ("Depth of field", "عمق الميدان"),
            ["renderer"] = ("3D renderer", "محرك العرض ثلاثي الأبعاد"),
            ["renderer_sw"] = ("ORIGINAL", "الأصلي"),
            ["renderer_gpu"] = ("REMASTER (GPU 3D)", "ريماستر (3D على كرت الشاشة)"),
            ["rm_things"] = ("Monsters and items", "الوحوش والأغراض"),
            ["rm_things_0"] = ("FLAT (ORIGINAL)", "مسطحة (الأصل)"),
            ["rm_things_1"] = ("3D VOXEL", "مجسمة (فوكسل)"),
            ["rm_shadows"] = ("Sun shadows", "ظلال الشمس"),
            ["dark_areas"] = ("Dark areas", "المناطق المظلمة"),
            ["dark_areas_0"] = ("ORIGINAL", "الأصلية"),
            ["dark_areas_1"] = ("LIFTED", "أوضح"),
            ["dark_areas_2"] = ("BRIGHT", "ساطعة"),
            ["fx_needs_light"] = ("World effects need Smooth lighting (page 1).", "مؤثرات العالم تحتاج الإضاءة الناعمة (الصفحة 1)."),
            ["gfx_next"] = ("More", "المزيد"),
            ["resolution"] = ("Resolution (max lines)", "الدقة (أقصى عدد أسطر)"),
            ["dynres"] = ("Dynamic resolution", "دقة ديناميكية"),
            ["smooth_light"] = ("Smooth lighting", "إضاءة ناعمة"),
            ["stable_perf"] = ("Stable performance", "أداء ثابت"),
            ["gfx_more"] = ("Effects", "المؤثرات"),
            ["gfx_open"] = ("OPEN", "فتح"),
            ["gfx_back"] = ("BACK", "رجوع"),
            ["bloom"] = ("Glow (bloom)", "التوهج"),
            ["lvl_0"] = ("OFF", "إيقاف"),
            ["lvl_1"] = ("LOW", "منخفض"),
            ["lvl_2"] = ("MEDIUM", "متوسط"),
            ["lvl_3"] = ("HIGH", "عالٍ"),
            ["grade"] = ("Colours", "الألوان"),
            ["grade_0"] = ("NEUTRAL", "طبيعية"),
            ["grade_1"] = ("VIVID", "حيوية"),
            ["grade_2"] = ("WARM", "دافئة"),
            ["vignette"] = ("Dark screen edges", "تعتيم الأطراف"),
            ["sharpen"] = ("Sharpen", "زيادة الحدة"),
            ["crt"] = ("Retro CRT screen", "شاشة CRT قديمة"),
            ["sidefill"] = ("Blurred side bars", "جوانب ضبابية"),
            ["look_sens"] = ("Look sensitivity", "حساسية النظر"),
            ["btn_size"] = ("Button size", "حجم الأزرار"),
            ["btn_alpha"] = ("Button opacity", "شفافية الأزرار"),
            ["left"] = ("Left-handed layout", "وضع اليد اليسرى"),
            ["run"] = ("Always run", "الركض الدائم"),
            ["edit"] = ("Button layout", "أماكن الأزرار"),
            ["edit_btn"] = ("EDIT", "تعديل"),
            ["gyro"] = ("Gyro aiming", "التصويب بالجيروسكوب"),
            ["gyro_sens"] = ("Gyro sensitivity", "حساسية الجيروسكوب"),
            ["gyro_inv"] = ("Invert gyro", "عكس اتجاه الجيروسكوب"),
            ["haptics"] = ("Vibration", "الاهتزاز"),
            ["smooth_look"] = ("Smooth look (every frame)", "نظر سلس (كل إطار)"),
            ["haptics_hint"] = ("Short pulses when you attack and when you are hit.", "نبضة قصيرة عند الهجوم وعند تلقي الضرر."),
            ["gyro_hint"] = ("Turn the phone to aim. Works together with swipe.", "حرّك الهاتف للتصويب، ويعمل مع السحب معاً."),
            ["fps"] = ("Frame rate", "معدل الإطارات"),
            ["auto"] = ("AUTO", "تلقائي"),
            ["wide"] = ("Widescreen", "شاشة عريضة"),
            ["hud"] = ("HUD", "الواجهة"),
            ["hud_bar"] = ("STATUS BAR", "شريط الحالة"),
            ["hud_full"] = ("FULLSCREEN", "شاشة كاملة"),
            ["hud_none"] = ("NONE", "بدون"),
            ["show_fps"] = ("Show FPS", "إظهار عدد الإطارات"),
            ["music"] = ("Music quality", "جودة الموسيقى"),
            ["music_hq"] = ("HIGH", "عالية"),
            ["music_classic"] = ("CLASSIC", "كلاسيكية"),
            ["lang"] = ("Language", "اللغة"),
            ["lang_name"] = ("ENGLISH", "العربية"),
            ["on"] = ("ON", "تشغيل"),
            ["off"] = ("OFF", "إيقاف"),
            ["done"] = ("DONE", "تم"),
            ["footer"] = ("Volume and brightness: game menu > OPTIONS", "مستوى الصوت والسطوع: قائمة اللعبة > OPTIONS"),
            ["tap_to_play"] = ("TAP TO PLAY", "اضغط للعب"),
            ["continue"] = ("CONTINUE", "متابعة"),
            ["loading"] = ("Loading REKKR…", "جارٍ تحميل REKKR…"),
            ["preparing"] = ("Preparing game data…", "جارٍ تجهيز ملفات اللعبة…"),
            ["editor_hint"] = ("Drag a button to move it. Tap it, then change its size.", "اسحب الزر لتحريكه، ثم اضغط عليه لتغيير حجمه."),
            ["size"] = ("SIZE", "الحجم"),
            ["reset"] = ("RESET", "افتراضي"),
            ["stick"] = ("MOVE STICK", "عصا الحركة"),
            // dev4
            ["free_look"] = ("Look up / down", "النظر للأعلى والأسفل"),
            ["invert_look"] = ("Invert look up/down", "عكس النظر العمودي"),
            ["autoaim"] = ("Auto-aim (vertical)", "التصويب التلقائي (عمودي)"),
            ["jump"] = ("Jump button", "زر القفز"),
            ["stick_mode"] = ("Joystick", "عصا التحكم"),
            ["stick_0"] = ("FIXED", "ثابتة"),
            ["stick_1"] = ("FLOATING", "متحركة"),
            ["crosshair"] = ("Crosshair", "علامة التصويب"),
            ["xh_0"] = ("+  BONE", "+  عاجي"),
            ["xh_1"] = ("+  RED", "+  أحمر"),
            ["xh_2"] = ("+  GREEN", "+  أخضر"),
            ["xh_3"] = ("DOT", "نقطة"),
            ["xh_4"] = ("OFF", "إيقاف"),
            ["more"] = ("Aim, jump & look", "التصويب والقفز والنظر"),
            ["look_hint"] = ("Swipe up/down to look. Double-tap to centre the view.", "اسحب للأعلى أو الأسفل للنظر. انقر مرتين لتوسيط النظر."),
            ["lbl_fire"] = ("ATTACK", "هجوم"),
            ["lbl_use"] = ("USE", "استخدام"),
            ["lbl_wnext"] = ("WEAPON", "سلاح"),
            ["lbl_wprev"] = ("WEAPON", "سلاح"),
            ["lbl_map"] = ("MAP", "خريطة"),
            ["lbl_run"] = ("RUN", "ركض"),
            ["lbl_ok"] = ("OK", "موافق"),
            ["lbl_back"] = ("BACK", "رجوع"),
            ["lbl_qsave"] = ("SAVE", "حفظ"),
            ["lbl_qload"] = ("LOAD", "تحميل"),
            ["lbl_jump"] = ("JUMP", "قفز"),
            ["saved"] = ("QUICK SAVE", "تم الحفظ السريع"),
            ["loaded"] = ("QUICK LOAD", "تم التحميل السريع"),
            ["map_follow"] = ("FOLLOW", "تتبع"),
        };

        private static readonly Dictionary<string, string> shaped = new Dictionary<string, string>();

        public static string T(string key)
        {
            if (!table.TryGetValue(key, out var v)) return key;
            return Arabic ? Shape(v.ar) : v.en;
        }

        public static string Shape(string s)
        {
            if (s == null) return null;
            if (!shaped.TryGetValue(s, out var r)) { r = ArabicShaper.Visual(s); shaped[s] = r; }
            return r;
        }
    }

    public static class ArabicShaper
    {
        // Presentation Forms-B: isolated form of each letter; 4-form letters use
        // iso, iso+1 (final), iso+2 (initial), iso+3 (medial); 2-form letters iso, iso+1.
        private static readonly Dictionary<char, (int iso, bool dual)> forms = new Dictionary<char, (int, bool)>
        {
            ['\u0621'] = (0xFE80, false), ['\u0622'] = (0xFE81, false), ['\u0623'] = (0xFE83, false),
            ['\u0624'] = (0xFE85, false), ['\u0625'] = (0xFE87, false), ['\u0626'] = (0xFE89, true),
            ['\u0627'] = (0xFE8D, false), ['\u0628'] = (0xFE8F, true), ['\u0629'] = (0xFE93, false),
            ['\u062A'] = (0xFE95, true), ['\u062B'] = (0xFE99, true), ['\u062C'] = (0xFE9D, true),
            ['\u062D'] = (0xFEA1, true), ['\u062E'] = (0xFEA5, true), ['\u062F'] = (0xFEA9, false),
            ['\u0630'] = (0xFEAB, false), ['\u0631'] = (0xFEAD, false), ['\u0632'] = (0xFEAF, false),
            ['\u0633'] = (0xFEB1, true), ['\u0634'] = (0xFEB5, true), ['\u0635'] = (0xFEB9, true),
            ['\u0636'] = (0xFEBD, true), ['\u0637'] = (0xFEC1, true), ['\u0638'] = (0xFEC5, true),
            ['\u0639'] = (0xFEC9, true), ['\u063A'] = (0xFECD, true), ['\u0641'] = (0xFED1, true),
            ['\u0642'] = (0xFED5, true), ['\u0643'] = (0xFED9, true), ['\u0644'] = (0xFEDD, true),
            ['\u0645'] = (0xFEE1, true), ['\u0646'] = (0xFEE5, true), ['\u0647'] = (0xFEE9, true),
            ['\u0648'] = (0xFEED, false), ['\u0649'] = (0xFEEF, false), ['\u064A'] = (0xFEF1, true),
        };

        private static bool IsLetter(char c) => forms.ContainsKey(c);
        private static bool IsHarakah(char c) => c >= '\u064B' && c <= '\u0652';

        private static int LamAlef(char alef)
        {
            switch (alef)
            {
                case '\u0622': return 0xFEF5;
                case '\u0623': return 0xFEF7;
                case '\u0625': return 0xFEF9;
                case '\u0627': return 0xFEFB;
            }
            return 0;
        }

        /// <summary>Logical-order string → shaped, visual-order (left-to-right) string.</summary>
        public static string Visual(string s)
        {
            bool any = false;
            foreach (var ch in s) if (IsLetter(ch)) { any = true; break; }
            if (!any) return s;

            // Drop diacritics (the UI strings do not use them).
            var src = new StringBuilder();
            foreach (var ch in s) if (!IsHarakah(ch) && ch != '\u0640') src.Append(ch);
            var t = src.ToString();

            var outp = new StringBuilder();
            for (var i = 0; i < t.Length; i++)
            {
                var c = t[i];
                if (!IsLetter(c)) { outp.Append(c); continue; }
                var prevJoins = i > 0 && IsLetter(t[i - 1]) && forms[t[i - 1]].dual;
                if (c == '\u0644' && i + 1 < t.Length && LamAlef(t[i + 1]) != 0)
                {
                    outp.Append((char)(LamAlef(t[i + 1]) + (prevJoins ? 1 : 0)));
                    i++;
                    continue;
                }
                var f = forms[c];
                var nextJoins = f.dual && i + 1 < t.Length && IsLetter(t[i + 1]);
                int cp;
                if (!f.dual) cp = f.iso + (prevJoins ? 1 : 0);
                else if (prevJoins && nextJoins) cp = f.iso + 3;
                else if (prevJoins) cp = f.iso + 1;
                else if (nextJoins) cp = f.iso + 2;
                else cp = f.iso;
                outp.Append((char)cp);
            }

            // Reverse for right-to-left display, keeping left-to-right runs (digits, Latin) intact.
            var logical = outp.ToString();
            var result = new StringBuilder(logical.Length);
            var i2 = logical.Length - 1;
            while (i2 >= 0)
            {
                if (IsLtr(logical[i2]))
                {
                    var end = i2;
                    while (i2 >= 0 && (IsLtr(logical[i2]) || (logical[i2] == ' ' && i2 > 0 && IsLtr(logical[i2 - 1]) && RunContinues(logical, i2)))) i2--;
                    result.Append(logical, i2 + 1, end - i2);
                }
                else
                {
                    result.Append(Mirror(logical[i2]));
                    i2--;
                }
            }
            return result.ToString();
        }

        private static bool RunContinues(string s, int spaceIndex)
        {
            // A space inside a Latin run ("OPTIONS MENU") stays inside the run.
            return spaceIndex + 1 < s.Length && IsLtr(s[spaceIndex + 1]);
        }

        private static bool IsLtr(char c) => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '%';

        private static char Mirror(char c)
        {
            switch (c)
            {
                case '(': return ')';
                case ')': return '(';
                case '<': return '>';
                case '>': return '<';
            }
            return c;
        }
    }
}
