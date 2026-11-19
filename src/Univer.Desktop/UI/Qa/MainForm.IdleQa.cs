using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // QA step "idle": a shown page that waits for nothing is not asked for its state every second (ProbePace), and is asked
    // at once when the paint gate reports a form or the user presses something on it. The app's own timer runs here, as it
    // does for a user; settledForQa stands in for the ten seconds a real page takes to settle.
    internal sealed partial class MainForm
    {
        private async Task IdleQaAsync(List<string> results)
        {
            while (!preparedBrowsers.Contains(main)) await Task.Delay(50);
            // A saved sign-in, as presentation QA has: without one the gate watches no forms and the user answers them.
            prefs.Language = "ru"; prefs.Readable = true; prefs.AutoLoginPaused = false; guard.Retry();
            credentials = new Credentials { Login = "fixture-account", Password = "synthetic fixture" };
            await RefreshDocumentScriptsAsync();
            SetPresentation(main, true);
            // The read notice is in the page but hidden, as the site keeps a form it shows later; its button submits nothing.
            const string notice = "<form id='attentionSt' hidden action='/student/attentionStudents/1'><input id='check' type='checkbox' onclick='document.getElementById(&quot;btn&quot;).disabled=false'><label for='check'>Я подтверждаю, что прочёл данное уведомление</label><button id='btn' type='submit' disabled onclick='event.preventDefault();this.closest(&quot;form&quot;).remove()'>Отправить</button></form>";
            main.LoadHtml(PolishShell("<p>QA</p><button id='qa-idle' type='button' style='display:block;width:240px;height:80px'>QA</button>" + notice, false), SitePolicy.Origin + "/news/student");
            await WaitQaPresentationAsync(); await ApplyPresentationAsync();
            var settling = 0;
            QaTrace("idle first quiet=" + automation.Quiet + " cover=" + loading.Visible);
            for (; settling < 5 && !(automation.Quiet && !loading.Visible); settling++) { await Task.Delay(200); await automation.TickAsync(main, credentials, prefs, false); }
            QaTrace("idle settling-ticks=" + settling + " quiet=" + automation.Quiet + " cover=" + loading.Visible + " page=" + await AutomationController.EvaluateAsync(main, "JSON.stringify([window.__univerTimingV1&&window.__univerTimingV1.wakes,window.__univerPaintV1&&window.__univerPaintV1.pending,document.readyState])"));
            Check(automation.Quiet && !loading.Visible, "a shown page that waits for nothing is quiet", results);
            Func<int, Func<bool>, Task<bool>> wait = async (milliseconds, done) =>
            {
                var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
                while (!done() && DateTime.UtcNow < until) await Task.Delay(25);
                return done();
            };
            settledForQa = true; automation.Woken(true); timer.Start();
            try
            {
                var before = automation.Ticks;
                await Task.Delay(3500);
                QaTrace("idle quiet-ticks=" + (automation.Ticks - before));
                Check(automation.Ticks == before && automation.Quiet, "a quiet page is not asked while the timer runs", results);
                // The paint gate covers the page and reports it, as it does when a known form appears.
                before = automation.Ticks;
                await AutomationController.EvaluateAsync(main, "window.__univerPaintV1.begin();'begun'");
                Check(await wait(1500, () => automation.Ticks > before && automation.Quiet && !loading.Visible), "the paint gate's report is answered at once and the page is uncovered again", results);
                automation.Woken(true); before = automation.Ticks;
                await Task.Delay(2500);
                Check(automation.Ticks == before, "the page is quiet again after the report", results);
                // A known form the site shows by itself: the gate's observer sees it, no press and no poll is needed.
                await AutomationController.EvaluateAsync(main, "document.getElementById('attentionSt').hidden=false;'shown'");
                Check(await wait(1500, () => automation.Ticks > before), "a known form that appears on a quiet page is answered at once", results);
                await AutomationController.EvaluateAsync(main, "var f=document.getElementById('attentionSt');if(f)f.remove();'gone'");
                Check(await wait(5000, () => automation.Quiet && !loading.Visible), "the page is quiet again once the form is gone", results);
                automation.Woken(true); before = automation.Ticks;
                var point = await AutomationController.EvaluateAsync(main, "(function(){var b=document.getElementById('qa-idle');window.qaIdle=0;b.addEventListener('pointerdown',function(){window.qaIdle++});var r=b.getBoundingClientRect();return Math.round(r.left+r.width/2)+'|'+Math.round(r.top+r.height/2)})()") ?? "";
                var at = point.Split('|'); int x = 0, y = 0;
                Check(at.Length == 2 && int.TryParse(at[0], out x) && int.TryParse(at[1], out y), "the fixture's button has a place", results);
                using (var client = main.GetDevToolsClient()) await PressQaPointAsync(client, x, y, "window.qaIdle>0");
                Check(await wait(1500, () => automation.Ticks > before), "a press on the page is followed by a tick at once", results);
                before = automation.Ticks;
                await Task.Delay(3200);
                QaTrace("idle woken-ticks=" + (automation.Ticks - before));
                Check(automation.Ticks - before >= 2 && automation.RecentInput, "after a press the page is asked every second for a while", results);
            }
            finally { timer.Stop(); settledForQa = false; credentials = null; prefs.AutoLoginPaused = true; guard.Retry(); messages.NewSession(); Localize(); }
        }
    }
}
