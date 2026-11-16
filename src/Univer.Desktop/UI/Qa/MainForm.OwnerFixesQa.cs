using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // The owner's third review of the copy, on synthetic pages with the site's menu as observed: pulling a tile at once, the
    // bin beside the grid, All sections as outlined rows with pins on the home tiles, a long New section list, the Period
    // list at the page's edge, words of controls that cannot be selected and even panel heads.
    internal sealed partial class MainForm
    {
        private static string FixesTool(string title, params string[] rows)
        { return "<table class='tool'><tr><td><ul>" + string.Join("", rows) + "</ul></td></tr><tr><td class='bottom-center'>" + title + "</td></tr></table>"; }
        private static string FixesRow(string href, string title, string count = "") { return "<li><a href='" + href + "'>" + title + "</a>" + count + "</li>"; }
        private static string FixesMenu
        {
            get
            {
                return "<tr id='qa-menu-row'><td><table class='inner'><tr><td class='center'><div id='menu-home'><table id='tools'><tr><td class='center'>" +
                    FixesTool("Студент", FixesRow("/student/rating/", "Анкетирование студентов", NoticeQaCount), FixesRow("/student/pers/", "Редактирование личных данных"), FixesRow("/student/practice/", "Практика")) +
                    FixesTool("Учебный процесс", FixesRow("/student/calendar/", "Академический календарь"), FixesRow("/student/myschedule/", "Расписание"), FixesRow("/student/attendance/full/", "Журнал посещений и успеваемости")) +
                    FixesTool("Документы", FixesRow("/student/transcript/", "Транскрипт"), FixesRow("/student/umkd/", "УМКД"), FixesRow("/student/igpa/", "IGPA")) +
                    FixesTool("Разное", FixesRow("/student/test/", "Тестирование"), FixesRow("/student/debts/", "Задолженности")) +
                    "</td></tr></table></div></td></tr></table></td></tr>";
            }
        }
        private async Task<string> FixesEvalAsync(string script) { return await AutomationController.EvaluateAsync(main, script) ?? ""; }
        private async Task FixesShotAsync(string qa, string name)
        { using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, name), (await client.Page.CaptureScreenshotAsync()).Data); }
        private void FixesEscape()
        {
            var host = main.GetBrowser().GetHost();
            host.SendKeyEvent(new KeyEvent { Type = KeyEventType.RawKeyDown, WindowsKeyCode = 27 }); host.SendKeyEvent(new KeyEvent { Type = KeyEventType.KeyUp, WindowsKeyCode = 27 });
        }
        private async Task OwnerFixesQaAsync(string qa, List<string> results)
        {
            prefs.Pins = new List<PinnedLink>(); prefs.TileOrder = new List<string>(); prefs.HiddenTiles = new List<string>();
            await RefreshDocumentScriptsAsync();
            var news = "<table class='inner'><tr><th>Дата</th><th>Новость</th></tr>" + NewsQaRow("09", "<strong>Встреча с участниками проектов</strong>") + NewsQaRow("08", "Короткое объявление") + "</table><table><tr><td class='tb'>Страница 1</td></tr></table>";
            SetPresentation(main, true); main.LoadHtml(PolishShell(news, false).Replace(PolishMenu, FixesMenu), SitePolicy.Origin + "/news/student"); await WaitQaPresentationAsync(); await ApplyPresentationAsync(); await Task.Delay(500);
            await FixesEvalAsync("window.__univerMotion=false;window.scrollTo(0,0);'top'");

            // A tile answers at once: pressing and pulling needs no waiting, and a held tile does not show a tooltip first.
            Check(await FixesEvalAsync("document.querySelector('.univer-app-tile>a').title===''&&!!document.querySelector('.univer-app-tile>a').getAttribute('aria-description')?'quiet':''") == "quiet", "a tile has no tooltip in the way of a long press", results);
            var tile = await PointOfAsync(".univer-app-quick .univer-app-tile:nth-child(2)>a");
            MouseAt(tile); await Task.Delay(40); Press(tile, false); await Task.Delay(30);
            MouseAt(new System.Drawing.Point(tile.X + 24, tile.Y + 6), CefEventFlags.LeftMouseButton); await Task.Delay(250);
            Check(await FixesEvalAsync("document.querySelector('.univer-app-tile.univer-app-lifted')?'up':''") == "up", "pulling a tile at once lifts it without waiting for the long press", results);

            // The bin is plain and close: right under the grid and as wide as it, tinted rather than loud, in room the page makes for it.
            Check(await FixesEvalAsync("(function(){var g=document.querySelector('.univer-app-quick').getBoundingClientRect(),n=document.querySelector('.univer-app-trash'),b=n.getBoundingClientRect(),s=getComputedStyle(n),t=document.querySelector('.univer-app-news-tools').getBoundingClientRect();return Math.abs(b.top-g.bottom-12)<=3&&Math.abs(b.width-g.width)<=3&&b.height<=46&&s.backgroundColor==='rgb(253, 236, 235)'&&s.color==='rgb(180, 35, 24)'&&t.top>=b.bottom?'near':b.top+'/'+g.bottom+'/'+b.width+'/'+g.width+'/'+b.height+'/'+s.backgroundColor+'/'+t.top;})()") == "near", "the bin is tinted, sits right under the tiles and covers nothing", results);
            await FixesShotAsync(qa, "product-bin-near.png");
            Press(await PointOfAsync(".univer-app-tile.univer-app-lifted>a"), true); await Task.Delay(400);

            // All sections: outlined rows, the count beside the name, pins in one column, and the home tiles have pins too.
            await FixesEvalAsync("document.querySelector('.univer-app-menu').open=true;'open'"); await Task.Delay(400);
            await FixesShotAsync(qa, "product-all-sections.png");
            var sections = await FixesEvalAsync("(function(){var rows=[];Array.from(document.querySelectorAll('.univer-app-category li')).filter(function(n){return n.offsetWidth>0;}).forEach(function(li){var two=li.querySelectorAll(':scope>.univer-app-pin-row');if(two.length>1)rows.push.apply(rows,two);else rows.push(li);});if(rows.length<9)return 'few '+rows.length;var bad=rows.filter(function(li){var s=getComputedStyle(li),p=li.querySelector('.univer-app-pin'),r=li.getBoundingClientRect(),x=p&&p.getBoundingClientRect();return s.borderTopWidth!=='1px'||s.display!=='grid'||r.height<40||!p||Math.abs(r.right-x.right)>8||x.top<r.top||x.bottom>r.bottom;});var count=document.querySelector('.univer-app-category li>b'),c=count.getBoundingClientRect(),l=count.closest('li').getBoundingClientRect();return !bad.length&&c.top>=l.top&&c.bottom<=l.bottom?'rows':bad.length+' bad: '+bad[0].textContent;})()"); QaTrace("qa-sections " + sections);
            Check(sections == "rows", "All sections shows outlined rows with the count beside the name and every pin at the row's end", results);
            var pin = ".univer-app-category a[href*=myschedule]~.univer-app-pin, .univer-app-category .univer-app-pin-row:has(a[href*=myschedule]) .univer-app-pin";
            Check(await FixesEvalAsync("document.querySelector('" + pin + "').getAttribute('aria-pressed')") == "true", "a home tile's section is shown as pinned", results);
            var at = await PointOfAsync(pin); Press(at, false); await Task.Delay(40); Press(at, true); await Task.Delay(400);
            Check(prefs.HiddenTiles.Count == 1 && await FixesEvalAsync("document.querySelector('.univer-app-quick a[href*=myschedule]')?'there':'gone'") == "gone", "unpinning a home tile in All sections takes it off the home screen", results);
            at = await PointOfAsync(pin); Press(at, false); await Task.Delay(40); Press(at, true); await Task.Delay(400);
            Check(prefs.HiddenTiles.Count == 0 && await FixesEvalAsync("document.querySelector('.univer-app-quick a[href*=myschedule]')?'there':'gone'") == "there", "pinning it again brings the tile back", results);
            await FixesEvalAsync("document.querySelector('.univer-app-menu').open=false;'closed'"); await Task.Delay(300);

            // New section stays on screen however long the list is.
            var add = await PointOfAsync(".univer-app-add-tile"); Press(add, false); await Task.Delay(40); Press(add, true); await Task.Delay(300);
            Check(await FixesEvalAsync("(function(){var l=document.querySelector('.univer-app-add-list');for(var i=0;i<60;i++){var p=document.createElement('p');p.style.height='48px';p.style.margin='0';l.appendChild(p);}var r=document.querySelector('.univer-app-add-panel').getBoundingClientRect(),h=document.documentElement.clientHeight;return r.top>=0&&r.bottom<=h&&Math.abs(r.top-(h-r.bottom))<=2&&l.scrollHeight>l.clientHeight?'centred':r.top+'/'+r.bottom+'/'+h;})()") == "centred", "New section stays in the middle of the window with a long list", results);
            await FixesShotAsync(qa, "product-new-section-long.png");
            FixesEscape(); await Task.Delay(200);

            // Period opens inside the page, and a highlighted control has room for its ring.
            var period = await PointOfAsync(".univer-app-news-period>summary"); Press(period, false); await Task.Delay(40); Press(period, true); await Task.Delay(300);
            Check(await FixesEvalAsync("(function(){var b=document.querySelector('.univer-app-news-period-box').getBoundingClientRect();return b.width>0&&b.left>=0&&b.right<=document.documentElement.clientWidth?'inside':b.left+'/'+b.right+'/'+document.documentElement.clientWidth;})()") == "inside", "the Period list opens inside the page", results);
            QaTrace("qa-news-clip " + await FixesEvalAsync("(function(){var n=document.querySelector('.univer-app-news-tools'),out=[];for(;n&&n!==document.documentElement;n=n.parentElement){var s=getComputedStyle(n);if(s.overflowX!=='visible'||s.overflowY!=='visible')out.push(n.tagName+'.'+n.className+':'+s.overflowX+'/'+s.overflowY);}return out.join(' ');})()"));
            Check(await FixesEvalAsync("parseFloat(getComputedStyle(document.querySelector('.univer-app-news-tools')).paddingTop)>=4?'room':''") == "room", "the news filters leave room for a control's highlight", results);
            await FixesShotAsync(qa, "product-period.png");
            Press(new System.Drawing.Point(20, 500), false); await Task.Delay(40); Press(new System.Drawing.Point(20, 500), true); await Task.Delay(200);

            // The words of a control are not text to select.
            Check(await FixesEvalAsync("['.univer-app-account>summary','.univer-app-tabs>a','.univer-app-news-important','.univer-app-add-tile'].every(function(s){var n=document.querySelector(s);return n&&getComputedStyle(n).userSelect==='none';})?'plain':''") == "plain", "the words on buttons, tabs and the account menu cannot be selected", results);

            // Panel heads line up, however the site's table starts its rows.
            var bachelor = "<table class='mt'><tr class='top'><td class='lt'></td><td class='ct'>Личные данные</td><td class='rt'></td></tr><tr><td class='ct'>Здравствуйте</td></tr><tr><td class='ct'>Обучение</td></tr><tr><td class='ct'>Форма обучения: очная</td></tr><tr><td class='ct'>Услуги</td></tr><tr><td class='ct'>Справки</td></tr></table>";
            SetPresentation(main, true); main.LoadHtml(PolishShell(bachelor, true), SitePolicy.Origin + "/student/bachelor/"); await WaitQaPresentationAsync(); await ApplyPresentationAsync(); await Task.Delay(500);
            Check(await FixesEvalAsync("(function(){var i=Array.from(document.querySelectorAll('.univer-app-panel-head>.univer-app-link-icon')).map(function(n){return Math.round(n.getBoundingClientRect().left);});return i.length>=3&&i.every(function(v){return v===i[0];})?'even':i.join();})()") == "even", "the icons of a panel's heads share one left edge", results);
            await FixesShotAsync(qa, "product-panel-heads.png");
            prefs.Pins = new List<PinnedLink>(); prefs.TileOrder = new List<string>(); prefs.HiddenTiles = new List<string>();
        }
    }
}
