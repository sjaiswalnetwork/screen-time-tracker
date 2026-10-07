using System.Collections.Generic;

// Interface language. English text is the key; Hindi is used for headings, tabs and menus when chosen in Settings.
static class Lang
{
    static readonly Dictionary<string, string> hi = new Dictionary<string, string>
    {
        { "Day", "दिन" }, { "Week", "सप्ताह" }, { "Month", "महीना" }, { "Log", "लॉग" }, { "Work", "काम" },
        { "Today", "आज" }, { "Yesterday", "कल" }, { "This week", "यह सप्ताह" }, { "This month", "यह महीना" },
        { "Screen Time", "स्क्रीन टाइम" }, { "SCREEN TIME", "स्क्रीन टाइम" }, { "MOST USED", "सबसे ज़्यादा" },
        { "APPS USED", "ऐप्स" }, { "APP SWITCHES", "ऐप बदलाव" }, { "UNLOCKS", "अनलॉक" }, { "TOTAL", "कुल" },
        { "BUSIEST DAY", "सबसे व्यस्त दिन" }, { "DAILY AVERAGE", "दैनिक औसत" }, { "INTERNET TIME", "इंटरनेट समय" },
        { "Timeline", "समयरेखा" }, { "Hourly activity", "घंटेवार गतिविधि" }, { "Categories", "श्रेणियाँ" },
        { "Websites", "वेबसाइटें" }, { "Pages, sheets & files", "पेज, शीट और फ़ाइलें" }, { "Apps", "ऐप्स" },
        { "Insights", "विश्लेषण" }, { "Log in & log out", "लॉग इन और लॉग आउट" }, { "Activity log", "गतिविधि लॉग" },
        { "Daily screen time", "दैनिक स्क्रीन टाइम" }, { "Projects & clients", "प्रोजेक्ट और क्लाइंट" },
        { "Attendance", "हाज़िरी" }, { "Offline work & breaks", "ऑफ़लाइन काम और ब्रेक" }, { "Calls & meetings", "कॉल और मीटिंग" },
        { "Calendar", "कैलेंडर" }, { "Focus score", "फोकस स्कोर" }, { "Deep work", "गहन काम" },
        { "Productive", "उत्पादक" }, { "Communication", "संचार" }, { "Entertainment", "मनोरंजन" }, { "Social", "सोशल" },
        { "Browsing", "ब्राउज़िंग" }, { "Other", "अन्य" },
        { "Open Screen Time", "स्क्रीन टाइम खोलें" }, { "Pause tracking", "ट्रैकिंग रोकें" }, { "Resume tracking", "ट्रैकिंग फिर शुरू करें" },
        { "Settings…", "सेटिंग्स…" }, { "Exit", "बंद करें" }, { "Export report (PDF)…", "रिपोर्ट (PDF) सहेजें…" },
        { "Focus mode", "फोकस मोड" }, { "Search history…", "इतिहास खोजें…" }, { "Add offline work…", "ऑफ़लाइन काम जोड़ें…" },
        { "Copy today's work note", "आज का काम-नोट कॉपी करें" }, { "Show mini widget", "मिनी विजेट दिखाएँ" },
        { "Make my Screen Time Wrapped card", "मेरा स्क्रीन टाइम कार्ड बनाएँ" }, { "Stop focus", "फोकस बंद करें" },
    };

    public static string T(string en)
    {
        string v;
        return Settings.Language == "hi" && hi.TryGetValue(en, out v) ? v : en;
    }
}
