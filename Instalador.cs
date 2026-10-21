// Instalador do Lume: um único .exe que leva o Lume.exe embutido.
// Instala só para o usuário atual (sem administrador) em %LOCALAPPDATA%\Programs\Lume,
// cria atalhos, registra em "Aplicativos instalados" e serve também de desinstalador.
// Uso silencioso: /silent [/dir=PASTA] [/nodesktop]   |   /uninstall [/silent] [/removedata]
// Compilado com o csc do .NET Framework 4 (C# 5).
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Instalador do Lume")]
[assembly: AssemblyDescription("Instalador do Lume - leitor de PDF, ePub e Mobi")]
[assembly: AssemblyProduct("Lume")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

static class Setup
{
    public const string Version = "1.3.0";
    const string UninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Lume";

    [STAThread]
    static int Main(string[] args)
    {
        bool uninstall = false, silent = false, desktop = true, removeData = false;
        string dir = null;
        foreach (string a in args)
        {
            string l = a.ToLowerInvariant();
            if (l == "/uninstall") uninstall = true;
            else if (l == "/silent") silent = true;
            else if (l == "/nodesktop") desktop = false;
            else if (l == "/removedata") removeData = true;
            else if (l.StartsWith("/dir=")) dir = a.Substring(5).Trim('"');
        }
        if (silent)
        {
            try
            {
                if (uninstall) Uninstall(removeData);
                else Install(dir ?? InstalledDir() ?? DefaultDir(), desktop);
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm(uninstall));
        return 0;
    }

    public static string DefaultDir()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Lume");
    }

    public static string InstalledDir()
    {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstKey))
        {
            object v = k == null ? null : k.GetValue("InstallLocation");
            return v == null ? null : v.ToString();
        }
    }

    public static string DataDir()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Lume");
    }

    public static byte[] Res(string name)
    {
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        using (MemoryStream m = new MemoryStream()) { s.CopyTo(m); return m.ToArray(); }
    }

    static string StartMenuLink() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Lume.lnk"); }
    static string DesktopLink() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Lume.lnk"); }

    // Fecha o Lume que estiver rodando a partir da pasta de instalação (para poder substituir o arquivo).
    static void StopLume(string dir)
    {
        foreach (Process p in Process.GetProcessesByName("Lume"))
        {
            try
            {
                string f = p.MainModule.FileName;
                if (string.Equals(Path.GetDirectoryName(f).TrimEnd('\\'), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    p.Kill(); p.WaitForExit(5000);
                }
            }
            catch { }
        }
    }

    static void WriteWithRetry(string path, byte[] data)
    {
        for (int i = 0; ; i++)
        {
            try { File.WriteAllBytes(path, data); return; }
            catch (IOException)
            {
                if (i >= 10) throw new IOException("Não foi possível substituir " + Path.GetFileName(path) + ". Feche o Lume e tente de novo.");
                Thread.Sleep(500);
            }
        }
    }

    static void Shortcut(string link, string target, string workDir)
    {
        Type t = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(t);
        object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
        Type lt = lnk.GetType();
        lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
        lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { workDir });
        lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target + ",0" });
        lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Lume - leitor de PDF, ePub e Mobi" });
        lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
    }

    public static string Install(string dir, bool desktopShortcut)
    {
        Directory.CreateDirectory(dir);
        StopLume(dir);
        string exe = Path.Combine(dir, "Lume.exe");
        WriteWithRetry(exe, Res("Lume.exe"));
        // Arquivos do WebView2 que a janela do Lume usa (ficam ao lado do Lume.exe)
        foreach (string dll in new[] { "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll" })
            WriteWithRetry(Path.Combine(dir, dll), Res(dll));

        // O próprio instalador fica na pasta como desinstalador.
        string un = Path.Combine(dir, "Desinstalar.exe");
        string self = Path.GetFullPath(Application.ExecutablePath);
        if (!string.Equals(self, Path.GetFullPath(un), StringComparison.OrdinalIgnoreCase)) File.Copy(self, un, true);

        Shortcut(StartMenuLink(), exe, dir);
        if (desktopShortcut) Shortcut(DesktopLink(), exe, dir);
        else if (File.Exists(DesktopLink())) File.Delete(DesktopLink());

        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstKey))
        {
            k.SetValue("DisplayName", "Lume");
            k.SetValue("DisplayVersion", Version);
            k.SetValue("Publisher", "Lume");
            k.SetValue("DisplayIcon", exe + ",0");
            k.SetValue("InstallLocation", dir);
            k.SetValue("UninstallString", "\"" + un + "\" /uninstall");
            k.SetValue("QuietUninstallString", "\"" + un + "\" /uninstall /silent");
            k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            long total = 0;
            foreach (string f in Directory.GetFiles(dir)) total += new FileInfo(f).Length;
            k.SetValue("EstimatedSize", (int)(total / 1024), RegistryValueKind.DWord);
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        return exe;
    }

    // O componente WebView2 da Microsoft (vem no Windows 10 e 11). Fica registrado no EdgeUpdate.
    public static bool HasWebView2()
    {
        const string id = @"\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        string[] keys = { @"SOFTWARE\WOW6432Node" + id, @"SOFTWARE" + id };
        foreach (RegistryKey root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            foreach (string k in keys)
                using (RegistryKey key = root.OpenSubKey(k))
                {
                    object pv = key == null ? null : key.GetValue("pv");
                    if (pv != null && pv.ToString() != "" && pv.ToString() != "0.0.0.0") return true;
                }
        return false;
    }

    public static void Uninstall(bool removeData)
    {
        string dir = InstalledDir() ?? Path.GetDirectoryName(Application.ExecutablePath);
        StopLume(dir);
        foreach (string l in new[] { StartMenuLink(), DesktopLink() }) if (File.Exists(l)) File.Delete(l);
        Registry.CurrentUser.DeleteSubKeyTree(UninstKey, false);
        string exe = Path.Combine(dir, "Lume.exe");
        if (File.Exists(exe)) File.Delete(exe);
        if (removeData && Directory.Exists(DataDir()))
        {
            try { Directory.Delete(DataDir(), true); }
            catch { throw new IOException("O Lume foi removido, mas alguns arquivos em Documentos\\Lume estão em uso. Feche a janela do Lume e apague essa pasta manualmente."); }
        }
        // O desinstalador não pode apagar a si mesmo enquanto roda: apaga a pasta logo depois de fechar.
        string self = Path.GetFullPath(Application.ExecutablePath);
        if (string.Equals(Path.GetDirectoryName(self).TrimEnd('\\'), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + dir + "\"");
            psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.WindowStyle = ProcessWindowStyle.Hidden;
            Process.Start(psi);
        }
        else if (Directory.Exists(dir))
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}

class SetupForm : Form
{
    readonly bool uninstall;
    readonly TextBox dirBox = new TextBox();
    readonly CheckBox desktopCheck = new CheckBox(), openCheck = new CheckBox(), dataCheck = new CheckBox();
    readonly Button primary = new Button(), cancel = new Button(), change = new Button();
    readonly Label status = new Label();
    readonly ProgressBar bar = new ProgressBar();
    string installedExe;
    bool done;

    static readonly Color Ink = Color.FromArgb(30, 34, 32), Muted = Color.FromArgb(106, 112, 108), Accent = Color.FromArgb(108, 143, 140);

    public SetupForm(bool uninstall)
    {
        this.uninstall = uninstall;
        bool update = !uninstall && Setup.InstalledDir() != null;
        Text = uninstall ? "Desinstalar o Lume" : update ? "Atualizar o Lume" : "Instalar o Lume";
        Font = new Font("Segoe UI", 9.75f);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 372);
        BackColor = Color.FromArgb(250, 247, 242);
        AutoScaleMode = AutoScaleMode.Dpi;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        // Faixa do topo, escura como o fundo do ícone
        Panel head = new Panel(); head.SetBounds(0, 0, 560, 112); head.BackColor = Color.FromArgb(31, 35, 49);
        PictureBox logo = new PictureBox(); logo.SetBounds(28, 20, 72, 72); logo.SizeMode = PictureBoxSizeMode.Zoom;
        try { logo.Image = Image.FromStream(new MemoryStream(Setup.Res("logo.png"))); } catch { }
        Label title = new Label(); title.Text = "Lume"; title.ForeColor = Color.White; title.Font = new Font("Segoe UI Semibold", 20f);
        title.SetBounds(114, 24, 400, 40); title.BackColor = Color.Transparent;
        Label sub = new Label(); sub.Text = "Leitor de PDF, ePub e Mobi  ·  versão " + Setup.Version; sub.ForeColor = Color.FromArgb(170, 190, 200);
        sub.SetBounds(116, 64, 420, 24); sub.BackColor = Color.Transparent;
        head.Controls.Add(logo); head.Controls.Add(title); head.Controls.Add(sub);
        Controls.Add(head);

        int y = 132;
        if (uninstall)
        {
            Label msg = Text2("O Lume será removido deste computador, com os atalhos e o registro em Aplicativos instalados.", 28, y, 504, 44);
            Controls.Add(msg); y += 56;
            dataCheck.Text = "Apagar também meus livros, anotações e progresso (Documentos\\Lume)";
            dataCheck.SetBounds(28, y, 504, 26); dataCheck.ForeColor = Color.FromArgb(170, 60, 45);
            Controls.Add(dataCheck); y += 30;
            Controls.Add(Text2("Se não marcar, seus livros continuam em Documentos\\Lume e voltam a aparecer se você instalar o Lume de novo.", 46, y, 486, 40, true));
            primary.Text = "Desinstalar";
        }
        else
        {
            Controls.Add(Text2("O Lume será instalado em:", 28, y, 504, 22)); y += 26;
            dirBox.Text = Setup.InstalledDir() ?? Setup.DefaultDir(); dirBox.ReadOnly = true; dirBox.SetBounds(28, y, 404, 26); dirBox.BackColor = Color.White;
            change.Text = "Alterar…"; change.SetBounds(440, y - 1, 92, 28); change.Click += ChangeDir;
            change.Enabled = !update; // numa atualização, mantém a pasta atual
            Controls.Add(dirBox); Controls.Add(change); y += 40;
            desktopCheck.Text = "Criar atalho na Área de Trabalho"; desktopCheck.Checked = true; desktopCheck.SetBounds(28, y, 504, 26); Controls.Add(desktopCheck); y += 28;
            openCheck.Text = "Abrir o Lume ao terminar"; openCheck.Checked = true; openCheck.SetBounds(28, y, 504, 26); Controls.Add(openCheck); y += 34;
            Controls.Add(Text2(Setup.HasWebView2()
                ? "Os livros importados ficam em Documentos\\Lume. Não é preciso ser administrador."
                : "Atenção: este computador não tem o componente Microsoft WebView2, que o Lume usa para mostrar os livros. Ao terminar, abriremos a página oficial da Microsoft para instalá-lo.", 28, y, 504, 40, true));
            primary.Text = update ? "Atualizar" : "Instalar";
        }

        status.SetBounds(28, 300, 330, 22); status.ForeColor = Muted; Controls.Add(status);
        bar.SetBounds(28, 324, 330, 8); bar.Style = ProgressBarStyle.Marquee; bar.Visible = false; Controls.Add(bar);

        cancel.Text = "Cancelar"; cancel.SetBounds(372, 318, 76, 32); cancel.Click += delegate { Close(); };
        primary.SetBounds(456, 318, 76, 32); primary.FlatStyle = FlatStyle.Flat; primary.FlatAppearance.BorderSize = 0;
        primary.BackColor = uninstall ? Color.FromArgb(199, 67, 47) : Accent; primary.ForeColor = Color.White;
        primary.Click += PrimaryClick;
        Controls.Add(cancel); Controls.Add(primary);
        AcceptButton = primary; CancelButton = cancel;
    }

    Label Text2(string text, int x, int y, int w, int h, bool small = false)
    {
        Label l = new Label(); l.Text = text; l.SetBounds(x, y, w, h);
        l.ForeColor = small ? Muted : Ink;
        if (small) l.Font = new Font("Segoe UI", 8.75f);
        return l;
    }

    void ChangeDir(object sender, EventArgs e)
    {
        using (FolderBrowserDialog d = new FolderBrowserDialog())
        {
            d.Description = "Escolha onde instalar o Lume (uma pasta \"Lume\" será criada dentro dela).";
            if (d.ShowDialog(this) != DialogResult.OK) return;
            string p = d.SelectedPath;
            dirBox.Text = Path.GetFileName(p.TrimEnd('\\')).Equals("Lume", StringComparison.OrdinalIgnoreCase) ? p : Path.Combine(p, "Lume");
        }
    }

    void PrimaryClick(object sender, EventArgs e)
    {
        if (done)
        {
            if (!uninstall && !Setup.HasWebView2()) { try { Process.Start("https://go.microsoft.com/fwlink/p/?LinkId=2124703"); } catch { } }
            else if (!uninstall && openCheck.Checked && installedExe != null) { try { Process.Start(installedExe); } catch { } }
            Close(); return;
        }
        primary.Enabled = cancel.Enabled = change.Enabled = false;
        bar.Visible = true; status.Text = uninstall ? "Removendo…" : "Instalando…";
        Application.DoEvents();
        try
        {
            if (uninstall) Setup.Uninstall(dataCheck.Checked);
            else installedExe = Setup.Install(dirBox.Text, desktopCheck.Checked);
            done = true;
            status.Text = uninstall ? "O Lume foi removido." : "Pronto! O Lume foi instalado.";
            status.ForeColor = uninstall ? Ink : Accent;
            primary.Text = "Concluir"; primary.Enabled = true; primary.BackColor = Accent;
            cancel.Visible = false;
        }
        catch (Exception ex)
        {
            status.Text = "Não foi possível concluir.";
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            primary.Enabled = cancel.Enabled = true; change.Enabled = !uninstall;
        }
        bar.Visible = false;
    }
}
