using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace Univer.Desktop.Core
{
    // Read-only page scripts for a course's UMKD page: where a clicked file stands (the course and the heading over it)
    // and whether an outside address really is among the page's materials. The caller runs them in the trusted main
    // frame; they change nothing in the page.
    internal static class StudyMaterials
    {
        public const string Found = "umkd";
        private const string OnCourse = @"top===self&&/^\/student\/umkd\/\d+\/\d+\/\d+\/?$/.test(location.pathname)";
        private static string Quote(string value) { return new JavaScriptSerializer().Serialize(value ?? ""); }
        // The heading row above a file link (`link`) names its section; the last breadcrumb names the course.
        private const string SectionOf = "var section='';for(var row=link.closest('tr')&&link.closest('tr').previousElementSibling;row;row=row.previousElementSibling)" +
            "if(row.cells.length===1&&row.cells[0].tagName==='TH'){section=row.cells[0].textContent;break;}";
        private const string CourseOf = "var crumbs=document.querySelectorAll('#l4 tr.top>td.ct a[href*=\"/student/umkd/\"]'),course=crumbs.length>1?crumbs[crumbs.length-1].textContent:'';";
        // "umkd" when the address is the target of a link in the materials, or is written out in one of their rows.
        public static string LinkCheck(string url)
        {
            return "(function(){if(!(" + OnCourse + "))return '';var target=" + Quote(url) + ";" +
                "var same=function(v){try{return new URL(v,location.href).href===new URL(target).href;}catch(_){return false;}};" +
                // The same reading of a written address as study-pages.js: sentence punctuation and an unpaired bracket are not part of it.
                "var written=function(v){for(;;){var c=v.replace(/[.,;:!?»\"']+$/,'');if(/\\)$/.test(c)&&c.split('(').length<c.split(')').length)c=c.slice(0,-1);if(c===v)return v;v=c;}};" +
                "return Array.from(document.querySelectorAll('#l4 table.mt')).some(function(t){if(t.closest('#tools'))return false;" +
                "return Array.from(t.querySelectorAll('a[href]')).some(function(a){return same(a.getAttribute('href'));})||" +
                "Array.from(t.querySelectorAll('tr.file>td,tr.link>td')).some(function(r){return (r.textContent.match(/https?:\\/\\/[^\\s<>\"']+/g)||[])" +
                ".some(function(u){return same(written(u));});});})?'" + Found + "':'';})()";
        }
        // {"course":…,"section":…} for the file link with this address: the last breadcrumb is the course, the nearest
        // heading row above the file is its section. "" away from a course page.
        public static string Place(string url)
        {
            return "(function(){if(!(" + OnCourse + "))return '';var target=" + Quote(url) + ";" +
                "var link=Array.from(document.querySelectorAll('#l4 a.downLoad[href]')).find(function(a){return a.href===target;});if(!link)return '';" +
                SectionOf + CourseOf + "return JSON.stringify({course:course,section:section});})()";
        }
        // «Скачать все»: {address: {name, course, section}} for each of these file links, read in one pass before the first
        // file, so the rest are named and filed correctly even if the student leaves the page meanwhile.
        public static string FilesScript(IList<string> urls)
        {
            return "(function(){if(!(" + OnCourse + "))return '';var wanted=" + new JavaScriptSerializer().Serialize(urls ?? new string[0]) + ",out={};" + CourseOf +
                "Array.from(document.querySelectorAll('#l4 a.downLoad[href]')).forEach(function(link){if(wanted.indexOf(link.href)<0||out[link.href])return;" +
                SectionOf + "out[link.href]={name:link.getAttribute('download')||link.textContent.trim(),course:course,section:section};});return JSON.stringify(out);})()";
        }
        public sealed class StudyFile { public string Name, Course, Section; }
        // What FilesScript answered; an empty map when the page gave nothing usable (the files then keep their own names).
        public static Dictionary<string, StudyFile> Files(string answer)
        {
            var files = new Dictionary<string, StudyFile>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(answer) || answer.Length > 1024 * 1024) return files;
            try
            {
                var values = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.Deserialize<Dictionary<string, object>>(answer);
                if (values == null) return files;
                foreach (var pair in values)
                {
                    var item = pair.Value as Dictionary<string, object>; if (item == null) continue;
                    object name, course, section;
                    item.TryGetValue("name", out name); item.TryGetValue("course", out course); item.TryGetValue("section", out section);
                    files[pair.Key] = new StudyFile { Name = name as string ?? "", Course = course as string, Section = section as string };
                }
            }
            catch (Exception) { files.Clear(); }
            return files;
        }
        // The folder under Downloads for what Place answered; the UMKD root when the page gave no course.
        public static string Folder(string place)
        {
            string course = null, section = null;
            if (!string.IsNullOrWhiteSpace(place) && place.Length <= 4096)
            {
                try
                {
                    var values = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(place);
                    object value;
                    if (values != null && values.TryGetValue("course", out value)) course = value as string;
                    if (values != null && values.TryGetValue("section", out value)) section = value as string;
                }
                catch (Exception) { course = section = null; }
            }
            return StudyFolders.Relative(course, section);
        }
    }
}
