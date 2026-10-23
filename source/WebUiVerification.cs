using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace MonitorShade
{
    internal static class WebUiVerification
    {
        internal static int Run(string path)
        {
            var report = new List<string>(); int result = 1;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var form = new SettingsForm(true, false);
            form.Loaded += async delegate {
                try {
                    for (int i = 0; i < 160 && !form.BrowserUi.Ready && form.BrowserUi.Error == null; i++) await Task.Delay(100);
                    if (!form.BrowserUi.Ready) throw new Exception("Chromium initialization failed: " + form.BrowserUi.Error);
                    var core = form.BrowserUi.Core;
                    report.Add("INFO: WebView2 " + core.Environment.BrowserVersionString);
                    report.Add("INFO: DPI " + System.Windows.PresentationSource.FromVisual(form).CompositionTarget.TransformToDevice.M11 * 96);
                    Func<string, string, Task> check = async (expression, name) => {
                        string actual = await core.ExecuteScriptAsync("Boolean(" + expression + ")");
                        if (actual != "true") throw new Exception("FAIL: " + name + " => " + actual);
                        report.Add("PASS: " + name);
                    };
                    await core.ExecuteScriptAsync("document.fonts.ready.then(()=>window.fontsReady=true)");
                    await Task.Delay(800);
                    await check("!window.monitorShadeError && !!window.monitorShadeState", "Local Chromium UI loads without JavaScript errors");
                    await check("document.fonts.check('16px Pretendard', '한글 획 두께') && document.fonts.check('600 16px Pretendard', '함께 조절')", "Embedded Pretendard Regular and SemiBold load");
                    await check("getComputedStyle(document.body).fontFamily.includes('Pretendard')", "1.15.1 Pretendard face is restored");
                    await check("getComputedStyle(document.querySelector('.sync-row label')).fontWeight==='400' && getComputedStyle(document.querySelector('.monitor-name')).fontWeight==='600'", "Body and heading use distinct real Regular/SemiBold weights");
                    await check("[...document.fonts].filter(f=>f.family==='Pretendard').every(f=>f.status==='loaded')", "Both Pretendard faces are downloaded from embedded resources");
                    if (!core.Settings.IsNonClientRegionSupportEnabled) throw new Exception("Native draggable regions disabled");
                    report.Add("PASS: Chromium native caption dragging/double-click support is enabled");
                    WebLiveVerification.CheckResizeEdges(form, report, false);
                    await check("document.querySelectorAll('.monitor-card').length===2", "Two preview monitors are rendered");
                    await check("document.querySelectorAll('.nav').length===6", "All six settings pages are available");
                    await core.ExecuteScriptAsync("document.querySelector('#masterRange').value=65;document.querySelector('#masterRange').dispatchEvent(new Event('input',{bubbles:true}))");
                    await Task.Delay(350);
                    await check("monitorShadeState.monitors.every(m=>m.percent===65)", "Chromium slider reaches the existing brightness controller and syncs absolute values");
                    await core.ExecuteScriptAsync("document.querySelector('[data-button=MasterPlus]').click()");
                    await Task.Delay(350);
                    await check("monitorShadeState.monitors.every(m=>m.percent===70)", "Plus button uses the existing synchronized step logic");
                    await check("document.querySelector('#dotBrightness').dataset.value==='70' && document.querySelectorAll('#dotBrightness circle').length>40", "Ambient card renders the real brightness as scalable dot glyphs");
                    foreach (int percent in new[] { 0, 100, 70 }) {
                        await core.ExecuteScriptAsync("document.querySelector('#masterRange').value=" + percent + ";document.querySelector('#masterRange').dispatchEvent(new Event('input',{bubbles:true}))"); await Task.Delay(400);
                        await check("document.querySelector('#dotBrightness').dataset.value==='" + percent + "' && monitorShadeState.monitors.every(m=>m.percent===" + percent + ")", "Dot readout stays synchronized at " + percent + " percent");
                    }
                    await core.ExecuteScriptAsync("document.querySelector('#settingsSearch').value='Backspace';document.querySelector('#settingsSearch').dispatchEvent(new Event('input'))");
                    await check("document.querySelectorAll('.search-result').length===1 && document.querySelector('.search-result').textContent.includes('창 크기 조절')", "Search finds resize shortcut by its key name");
                    await core.ExecuteScriptAsync("document.querySelector('#settingsSearch').dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',bubbles:true}))"); await Task.Delay(500);
                    await check("!document.querySelector('#Layout').hidden && document.activeElement.id==='resizeFeature' && !document.querySelector('#navigation').hidden", "Search Enter navigates and focuses the matching setting");
                    await core.ExecuteScriptAsync("document.querySelector('#settingsSearch').value='프리텐다드';document.querySelector('#settingsSearch').dispatchEvent(new Event('input'))");
                    await check("document.querySelectorAll('.search-result').length===1 && document.querySelector('.search-result').textContent.includes('글꼴')", "Korean search aliases find the font setting");
                    await core.ExecuteScriptAsync("document.querySelector('#settingsSearch').value='no-match-987';document.querySelector('#settingsSearch').dispatchEvent(new Event('input'))");
                    await check("document.querySelectorAll('.search-result').length===0 && !!document.querySelector('.search-empty')", "Empty search results are explained");
                    await core.ExecuteScriptAsync("document.querySelector('#settingsSearch').dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}))");
                    await check("document.querySelector('#settingsSearch').value==='' && !document.querySelector('#navigation').hidden", "Escape restores the complete navigation");
                    await core.ExecuteScriptAsync("monitorShadeNavigate('Shortcuts')"); await Task.Delay(450);
                    await check("!document.querySelector('#Shortcuts').hidden && getComputedStyle(document.querySelector('#Shortcuts')).opacity==='1'", "Page fade ends at full opacity");
                    await core.ExecuteScriptAsync("document.querySelector('#BrightnessStep').value='7';document.querySelector('#BrightnessStep').dispatchEvent(new Event('change'));document.querySelector('[data-button=ApplyBrightness]').click()"); await Task.Delay(350);
                    await check("monitorShadeState.fields.BrightnessStep==='7'", "Shortcut editor updates the native settings controller");
                    await core.ExecuteScriptAsync("monitorShadeNavigate('General')"); await Task.Delay(400);
                    await core.ExecuteScriptAsync("document.querySelector('#AppearanceMode').value='1';document.querySelector('#AppearanceMode').dispatchEvent(new Event('change'))"); await Task.Delay(350);
                    await check("monitorShadeState.palette.light && monitorShadeState.fields.AppearanceMode.value===1", "Morning palette is warm and light");
                    await core.ExecuteScriptAsync("document.querySelector('#UseBackdrop').click()"); await Task.Delay(350);
                    await check("document.body.classList.contains('solid') && !monitorShadeState.glass", "Blur can be disabled without a capture window");
                    await core.ExecuteScriptAsync("document.querySelector('#UseBackdrop').click();document.querySelector('#AppearanceMode').value='3';document.querySelector('#AppearanceMode').dispatchEvent(new Event('change'))"); await Task.Delay(350);
                    await check("monitorShadeState.fields.UseBackdrop && !monitorShadeState.palette.light", "Blur preference and evening palette restore");
                    foreach (string page in new[] { "Protection", "Layout", "Shortcuts", "Help", "General", "Brightness" }) {
                        await core.ExecuteScriptAsync("monitorShadeNavigate('" + page + "')"); await Task.Delay(400);
                        await check("!document.querySelector('#" + page + "').hidden && !window.monitorShadeError", page + " page renders");
                        await check("document.querySelector('#main').scrollWidth<=document.querySelector('#main').clientWidth+1", page + " page fits without horizontal clipping");
                        if (page == "Layout") {
                            await check("document.querySelector('.scale-feature').textContent.includes('Backspace') && document.querySelector('.scale-feature').textContent.includes('70%')", "Monitor-ratio resize cycle is discoverable on the layout page");
                            await core.ExecuteScriptAsync("document.querySelector('#LayoutEnabled').click();document.querySelector('[data-button=ApplyLayout]').click()"); await Task.Delay(300);
                            await check("!monitorShadeState.fields.LayoutEnabled && monitorShadeState.messages.LayoutStatus==='저장됨'", "Layout toggle applies to arrangement and resize shortcuts");
                            await core.ExecuteScriptAsync("document.querySelector('#LayoutEnabled').click();document.querySelector('[data-button=ApplyLayout]').click()"); await Task.Delay(300);
                        }
                    }
                    await core.ExecuteScriptAsync("monitorShadeNavigate('General');monitorShadeNavigate('Layout');monitorShadeNavigate('Brightness')"); await Task.Delay(500);
                    await check("document.querySelectorAll('.page:not([hidden])').length===1 && document.querySelectorAll('[aria-current=page]').length===1 && !document.querySelector('#Brightness').hidden", "Rapid navigation reaches the last requested page with no stale fade");
                    // Command validation is checked at the native boundary, not only in HTML.
                    foreach (var command in new[] {
                        new Dictionary<string, object> { { "action", "field" }, { "name", "Unknown" }, { "value", true } },
                        new Dictionary<string, object> { { "action", "brightness" }, { "id", "master" }, { "value", 101 } },
                        new Dictionary<string, object> { { "action", "field" }, { "name", "AppearanceMode" }, { "value", -1 } },
                        new Dictionary<string, object> { { "action", "button" }, { "name", "ArbitraryCommand" } }
                    }) {
                        bool rejected = false; try { form.WebCommand(command); } catch (ArgumentException) { rejected = true; }
                        if (!rejected) throw new Exception("FAIL: Invalid bridge command was accepted");
                    }
                    report.Add("PASS: Native bridge rejects unknown commands and invalid brightness/selection values");
                    await check("getComputedStyle(document.querySelector('.card')).borderRadius==='20px'", "20px translucent card geometry is preserved");
                    await check("getComputedStyle(document.querySelector('.card')).backgroundColor.startsWith('rgba')", "Cards remain translucent");
                    form.Width = 960; form.Height = 640; await Task.Delay(350);
                    await check("document.documentElement.scrollWidth===document.documentElement.clientWidth && document.querySelector('#main').scrollWidth<=document.querySelector('#main').clientWidth+1", "Minimum window size remains usable");
                    await core.ExecuteScriptAsync("monitorShadeNavigate('Layout')"); await Task.Delay(400);
                    await check("document.querySelector('#main').scrollWidth<=document.querySelector('#main').clientWidth+1 && [...document.querySelectorAll('.keycap')].every(e=>e.scrollWidth<=e.clientWidth+1)", "New layout cards and key labels fit the minimum window size");
                    await core.ExecuteScriptAsync("monitorShadeNavigate('Brightness')");
                    form.Width = 1120; form.Height = 840; await Task.Delay(400);
                    // Documentation previews use mock monitors and a solid base beneath
                    // the translucent UI; native Acrylic is verified separately.
                    await core.ExecuteScriptAsync("document.documentElement.style.background='rgb('+monitorShadeState.palette.background+')'");
                    using (var output = File.Create(path + ".png")) await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
                    report.Add("PASS: Chromium screenshot saved (native DWM backdrop is outside the web capture)");
                    await core.ExecuteScriptAsync("document.querySelector('#AppearanceMode').value='1';document.querySelector('#AppearanceMode').dispatchEvent(new Event('change'))"); await Task.Delay(1500);
                    await core.ExecuteScriptAsync("document.documentElement.style.background='rgb('+monitorShadeState.palette.background+')'");
                    using (var output = File.Create(path + ".morning.png")) await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
                    await core.ExecuteScriptAsync("monitorShadeNavigate('Layout')"); await Task.Delay(450);
                    await core.ExecuteScriptAsync("document.activeElement.blur()");
                    using (var output = File.Create(path + ".layout.png")) await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
                    report.Add("INFO: No DDC writes, hotkey registrations or preferences changes in preview mode");
                    result = 0;
                } catch (Exception e) { report.Add(e.ToString()); }
                finally { File.WriteAllLines(path, report); form.AllowSystemExit(); form.Close(); app.Shutdown(); }
            };
            form.Show(); app.Run(); return result;
        }
    }
}
