// Lume para Windows: janela própria (WebView2) + servidor local que guarda os livros em Documentos\Lume.
// Não abre o navegador Edge: a interface roda dentro da janela do Lume, com o componente WebView2 do Windows.
// Compilado com o csc do .NET Framework 4 (C# 5), por isso sem sintaxe mais nova.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Nome e descrição que o Windows mostra no Gerenciador de Tarefas e nas propriedades do arquivo.
[assembly: AssemblyTitle("Lume")]
[assembly: AssemblyDescription("Lume - leitor de PDF, ePub e Mobi")]
[assembly: AssemblyProduct("Lume")]
[assembly: AssemblyVersion("1.5.0.0")]
[assembly: AssemblyFileVersion("1.5.0.0")]

static class Program
{
    const int Port = 47812; // porta fixa: as preferências da interface ficam guardadas por endereço
    public static readonly string Origin = "http://localhost:" + Port;
    public static string Root, BooksDir, NotesDir, FilesDir, WebDir;
    public static MainForm Window;
    static readonly Regex IdRx = new Regex("^[a-z0-9]{4,40}$");
    static readonly Regex CdnRx = new Regex("^(cdnjs|jsd)/[A-Za-z0-9._@/+-]+$");

    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        // Nitidez em telas com escala (125%, 150%...)
        try { if (!SetProcessDpiAwarenessContext(new IntPtr(-4))) SetProcessDPIAware(); } catch { try { SetProcessDPIAware(); } catch { } }

        Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Lume");
        FilesDir = Path.Combine(Root, "Livros");
        BooksDir = Path.Combine(Root, "dados", "livros");
        NotesDir = Path.Combine(Root, "dados", "anotacoes");
        // Cache do WebView2 fica fora de Documentos (que pode estar sincronizado com o OneDrive)
        WebDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lume", "WebView2");
        foreach (string d in new[] { FilesDir, BooksDir, NotesDir, WebDir }) Directory.CreateDirectory(d);
        // Perfil do Edge usado pelas versões antigas do Lume: não é mais necessário
        try { string old = Path.Combine(Root, "dados", "janela"); if (Directory.Exists(old)) Directory.Delete(old, true); } catch { }

        HttpListener listener = new HttpListener();
        listener.Prefixes.Add(Origin + "/");
        try { listener.Start(); }
        catch
        {
            // O Lume já está aberto: traz a janela dele para a frente.
            FocusRunning();
            return;
        }
        Thread server = new Thread(delegate () { Serve(listener); });
        server.IsBackground = true;
        server.Start();

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Window = new MainForm();
        Application.Run(Window);
        try { listener.Stop(); } catch { }
        Environment.Exit(0);
    }

    static void FocusRunning()
    {
        try
        {
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(Origin + "/api/focus");
            r.Method = "POST"; r.ContentLength = 0; r.Timeout = 3000; r.Proxy = null;
            using (r.GetResponse()) { }
        }
        catch { MessageBox.Show("O Lume já está aberto.", "Lume", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }

    static void Serve(HttpListener l)
    {
        while (l.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = l.GetContext(); } catch { break; }
            ThreadPool.QueueUserWorkItem(delegate (object o) { Handle((HttpListenerContext)o); }, ctx);
        }
    }

    static void Handle(HttpListenerContext c)
    {
        try
        {
            // Só atende a própria janela do Lume (bloqueia sites tentando acessar o servidor local).
            string host = c.Request.Headers["Host"] ?? "";
            string origin = c.Request.Headers["Origin"];
            if (host != "localhost:" + Port || (origin != null && origin != Origin)) { Status(c, 403); return; }

            string path = c.Request.Url.AbsolutePath;
            if (path == "/" || path == "/index.html") Send(c, Encoding.UTF8.GetBytes(Page()), "text/html; charset=utf-8", false);
            else if (path == "/icon.png") Send(c, Resource("icon.png"), "image/png", false);
            else if (path.StartsWith("/cdn/")) Cdn(c, path.Substring(5));
            else if (path.StartsWith("/api/")) Api(c, path.Substring(5).Split('/'));
            else Status(c, 404);
        }
        catch (Exception)
        {
            try { Status(c, 500); } catch { }
        }
    }

    static byte[] Resource(string name)
    {
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        using (MemoryStream m = new MemoryStream()) { s.CopyTo(m); return m.ToArray(); }
    }

    static string Page()
    {
        string html = Encoding.UTF8.GetString(Resource("index.html"));
        html = html.Replace("https://cdnjs.cloudflare.com/ajax/libs/", "/cdn/cdnjs/").Replace("https://cdn.jsdelivr.net/npm/", "/cdn/jsd/");
        return "<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1,viewport-fit=cover\">"
            + "<link rel=\"icon\" type=\"image/png\" sizes=\"256x256\" href=\"/icon.png\">"
            + "<script>window.ESTANTE_DESKTOP=" + Json(Root) + ";</script>"
            + "<style>body{margin:0}img{max-width:100%}</style></head><body>" + html + "</body></html>";
    }

    // Bibliotecas de leitura (pdf.js, epub.js, JSZip): vêm embutidas no programa na hora de compilar.
    // O Lume.exe não baixa nada da internet.
    static void Cdn(HttpListenerContext c, string rest)
    {
        if (!CdnRx.IsMatch(rest) || rest.Contains("..")) { Status(c, 400); return; }
        Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("cdn/" + rest);
        if (s == null) { Status(c, 404); return; }
        s.Dispose();
        Send(c, Resource("cdn/" + rest), "application/javascript; charset=utf-8", true);
    }

    static void Api(HttpListenerContext c, string[] parts)
    {
        string m = c.Request.HttpMethod;
        string store = parts[0];
        if (store == "focus" && m == "POST")
        {
            MainForm w = Window;
            if (w != null) w.BeginInvoke((MethodInvoker)delegate { w.ShowFront(); });
            Status(c, 204); return;
        }
        if (store == "presence")
        {
            if (m == "PUT")
            {
                string body;
                using (StreamReader r = new StreamReader(c.Request.InputStream, Encoding.UTF8)) body = r.ReadToEnd();
                string[] lines = body.Split('\n');
                DiscordPresence.Set(lines[0], lines.Length > 1 ? lines[1] : "");
            }
            else DiscordPresence.Clear();
            Status(c, 204); return;
        }
        if (store == "open-folder" && m == "POST")
        {
            Process.Start("explorer.exe", "\"" + Root + "\"");
            Status(c, 204); return;
        }
        string dir = store == "books" ? BooksDir : store == "notes" ? NotesDir : store == "files" ? FilesDir : null;
        if (dir == null) { Status(c, 404); return; }

        if (parts.Length == 1 && m == "GET" && store != "files")
        {
            StringBuilder sb = new StringBuilder("[");
            bool first = true;
            foreach (string f in Directory.GetFiles(dir, "*.json"))
            {
                string txt;
                try { txt = File.ReadAllText(f, Encoding.UTF8).Trim(); } catch { continue; }
                if (txt.Length == 0) continue;
                if (!first) sb.Append(',');
                sb.Append(txt); first = false;
            }
            sb.Append(']');
            Send(c, Encoding.UTF8.GetBytes(sb.ToString()), "application/json; charset=utf-8", false);
            return;
        }
        if (parts.Length != 2 || !IdRx.IsMatch(parts[1])) { Status(c, 400); return; }
        string id = parts[1];

        if (store == "files")
        {
            string[] existing = Directory.GetFiles(FilesDir, id + ".*");
            if (m == "GET")
            {
                if (existing.Length == 0) { Status(c, 404); return; }
                Send(c, File.ReadAllBytes(existing[0]), "application/octet-stream", false);
            }
            else if (m == "PUT")
            {
                string ext = c.Request.QueryString["ext"];
                if (ext != "pdf" && ext != "epub" && ext != "mobi") ext = "bin";
                string target = Path.Combine(FilesDir, id + "." + ext), tmp = target + ".tmp";
                using (FileStream fs = File.Create(tmp)) c.Request.InputStream.CopyTo(fs);
                foreach (string f in existing) File.Delete(f);
                File.Move(tmp, target);
                Status(c, 204);
            }
            else if (m == "DELETE")
            {
                foreach (string f in existing) File.Delete(f);
                Status(c, 204);
            }
            else Status(c, 405);
            return;
        }

        string file = Path.Combine(dir, id + ".json");
        if (m == "GET")
        {
            if (!File.Exists(file)) { Status(c, 404); return; }
            Send(c, File.ReadAllBytes(file), "application/json; charset=utf-8", false);
        }
        else if (m == "PUT")
        {
            string body;
            using (StreamReader r = new StreamReader(c.Request.InputStream, Encoding.UTF8)) body = r.ReadToEnd();
            if (body.Length == 0 || body[0] != '{') { Status(c, 400); return; }
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, body, new UTF8Encoding(false));
            File.Copy(tmp, file, true);
            File.Delete(tmp);
            Status(c, 204);
        }
        else if (m == "DELETE")
        {
            if (File.Exists(file)) File.Delete(file);
            Status(c, 204);
        }
        else Status(c, 405);
    }

    public static string Json(string s)
    {
        StringBuilder sb = new StringBuilder("\"");
        foreach (char ch in s)
        {
            if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
            else if (ch < 32 || ch == '<' || ch == '>') sb.Append("\\u").Append(((int)ch).ToString("x4"));
            else sb.Append(ch);
        }
        return sb.Append('"').ToString();
    }

    static void Send(HttpListenerContext c, byte[] b, string type, bool cache)
    {
        c.Response.ContentType = type;
        c.Response.Headers["Cache-Control"] = cache ? "max-age=31536000, immutable" : "no-store";
        c.Response.ContentLength64 = b.Length;
        c.Response.OutputStream.Write(b, 0, b.Length);
        c.Response.Close();
    }

    static void Status(HttpListenerContext c, int code)
    {
        c.Response.StatusCode = code;
        c.Response.Close();
    }
}

// Discord Rich Presence: mostra no perfil "Lendo" (nome do aplicativo no Discord), o autor e o título.
// Conversa com o Discord pelo pipe local \\.\pipe\discord-ipc-N (sem internet, sem biblioteca extra).
// Se o Discord não estiver aberto, tenta de novo a cada 15 segundos enquanto houver um livro aberto.
static class DiscordPresence
{
    // ID do aplicativo "Lendo" no Portal de Desenvolvedores do Discord (não é segredo).
    const string ClientId = "1562587405536600165";
    static readonly object Lock = new object();
    static readonly AutoResetEvent Wake = new AutoResetEvent(false);
    static string wanted;            // atividade desejada (JSON) ou null para limpar
    static Thread worker;
    static NamedPipeClientStream pipe;

    public static void Set(string title, string author)
    {
        string details = Field(author.Trim().Length > 0 ? author : "Autor desconhecido");
        string state = Field(title.Trim().Length > 0 ? title : "Sem título");
        lock (Lock) wanted = "{\"details\":" + Program.Json(details) + ",\"state\":" + Program.Json(state) + ",\"type\":0,\"instance\":false}";
        Start(); Wake.Set();
    }

    public static void Clear()
    {
        lock (Lock) wanted = null;
        Wake.Set();
    }

    // O Discord exige de 2 a 128 caracteres por linha
    static string Field(string s)
    {
        s = s.Trim();
        if (s.Length > 128) s = s.Substring(0, 125) + "...";
        while (s.Length < 2) s += "\u2800";
        return s;
    }

    static void Start()
    {
        if (ClientId.Length == 0) return;
        lock (Lock)
        {
            if (worker != null) return;
            worker = new Thread(Loop); worker.IsBackground = true; worker.Start();
        }
    }

    static void Loop()
    {
        string sent = "\u0001"; // nada enviado ainda
        while (true)
        {
            string want;
            lock (Lock) want = wanted;
            try
            {
                if (pipe == null || !pipe.IsConnected)
                {
                    if (want == null) { sent = null; Wake.WaitOne(); continue; } // nada para mostrar: nem conecta
                    Connect(); sent = "\u0001";
                }
                if (want != sent)
                {
                    Write(1, "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" + Process.GetCurrentProcess().Id
                        + ",\"activity\":" + (want ?? "null") + "},\"nonce\":\"" + Guid.NewGuid() + "\"}");
                    string resp = Read();
                    Log((want == null ? "atividade limpa: " : "atividade enviada: ") + resp);
                    sent = want;
                }
            }
            catch (Exception e) { Log("sem conexão: " + e.Message); Disconnect(); sent = "\u0001"; }
            Wake.WaitOne(15000);
        }
    }

    static void Connect()
    {
        for (int i = 0; i < 10; i++)
        {
            NamedPipeClientStream p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut);
            try
            {
                p.Connect(300);
                pipe = p;
                Write(0, "{\"v\":1,\"client_id\":\"" + ClientId + "\"}");
                Read(); // READY
                return;
            }
            catch { try { p.Dispose(); } catch { } pipe = null; }
        }
        throw new IOException("Discord não encontrado");
    }

    // Registro curto em %LOCALAPPDATA%\Lume\discord.log, para entender problemas de conexão
    static void Log(string msg)
    {
        try
        {
            string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lume", "discord.log");
            if (File.Exists(f) && new FileInfo(f).Length > 64 * 1024) File.Delete(f);
            File.AppendAllText(f, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + (msg.Length > 400 ? msg.Substring(0, 400) : msg) + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }

    static void Disconnect()
    {
        try { if (pipe != null) pipe.Dispose(); } catch { }
        pipe = null;
    }

    static void Write(int op, string json)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        byte[] frame = new byte[8 + body.Length];
        BitConverter.GetBytes(op).CopyTo(frame, 0);
        BitConverter.GetBytes(body.Length).CopyTo(frame, 4);
        body.CopyTo(frame, 8);
        pipe.Write(frame, 0, frame.Length);
        pipe.Flush();
    }

    static string Read()
    {
        byte[] head = ReadExactly(8);
        int op = BitConverter.ToInt32(head, 0), len = BitConverter.ToInt32(head, 4);
        string json = Encoding.UTF8.GetString(ReadExactly(len));
        if (op == 2) throw new IOException("Discord fechou a conexão: " + json); // ex.: ID do aplicativo inválido
        return json;
    }

    static byte[] ReadExactly(int n)
    {
        byte[] b = new byte[n];
        int got = 0;
        while (got < n)
        {
            int r = pipe.Read(b, got, n - got);
            if (r <= 0) throw new IOException("conexão com o Discord encerrada");
            got += r;
        }
        return b;
    }
}

// A janela do Lume: um WebView2 ocupando a janela inteira.
class MainForm : Form
{
    readonly WebView2 web = new WebView2();
    readonly string boundsFile = Path.Combine(Program.Root, "dados", "janela.txt");
    static readonly Color Paper = Color.FromArgb(250, 247, 242);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    bool full;
    FormWindowState prevState;
    FormBorderStyle prevBorder;

    public MainForm()
    {
        Text = "Lume";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        BackColor = Paper;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(720, 520);
        LoadBounds();
        web.Dock = DockStyle.Fill;
        web.DefaultBackgroundColor = Paper;
        Controls.Add(web);
        Load += async delegate (object s, EventArgs e)
        {
            // Se o Lume foi iniciado "oculto" (por um atalho ou outro programa), mostra a janela mesmo assim
            BeginInvoke((MethodInvoker)delegate { ShowWindow(Handle, WindowState == FormWindowState.Maximized ? 3 : 5); Activate(); });
            await Init();
        };
        FormClosing += delegate (object s, FormClosingEventArgs e) { SaveBounds(); };
    }

    async Task Init()
    {
        try
        {
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, Program.WebDir, null);
            await web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException) { MissingRuntime(); return; }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível abrir o Lume: " + ex.Message, "Lume", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close(); return;
        }
        CoreWebView2 core = web.CoreWebView2;
        CoreWebView2Settings st = core.Settings;
        st.AreDevToolsEnabled = false;
        st.IsStatusBarEnabled = false;
        st.AreBrowserAcceleratorKeysEnabled = false; // sem F5, Ctrl+P, Ctrl+F etc. de navegador
        st.IsSwipeNavigationEnabled = false;
        st.IsGeneralAutofillEnabled = false;
        st.IsPasswordAutosaveEnabled = false;
        // Sobre o texto, só as opções do Lume. Em campos de digitação o menu de recortar/colar continua.
        core.ContextMenuRequested += delegate (object o, CoreWebView2ContextMenuRequestedEventArgs a) { if (!a.ContextMenuTarget.IsEditable) a.Handled = true; };
        // Links externos abrem no navegador padrão, fora do Lume
        core.NewWindowRequested += delegate (object o, CoreWebView2NewWindowRequestedEventArgs a) { a.Handled = true; OpenExternal(a.Uri); };
        core.NavigationStarting += delegate (object o, CoreWebView2NavigationStartingEventArgs a)
        {
            if (!a.Uri.StartsWith(Program.Origin + "/")) { a.Cancel = true; OpenExternal(a.Uri); }
        };
        core.ContainsFullScreenElementChanged += delegate { SetFull(core.ContainsFullScreenElement); };
        core.Navigate(Program.Origin + "/");
    }

    static void OpenExternal(string uri)
    {
        if (uri != null && (uri.StartsWith("https://") || uri.StartsWith("http://")))
            try { Process.Start(uri); } catch { }
    }

    void MissingRuntime()
    {
        DialogResult r = MessageBox.Show(this,
            "O Lume precisa do componente Microsoft WebView2, que já vem no Windows 10 e 11, mas não foi encontrado neste computador.\n\nAbrir a página oficial da Microsoft para instalá-lo?",
            "Lume", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (r == DialogResult.Yes) OpenExternal("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
        Close();
    }

    public void ShowFront()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate(); BringToFront();
    }

    void SetFull(bool on)
    {
        if (on && !full)
        {
            prevState = WindowState; prevBorder = FormBorderStyle;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal; WindowState = FormWindowState.Maximized;
            full = true;
        }
        else if (!on && full)
        {
            FormBorderStyle = prevBorder; WindowState = prevState;
            full = false;
        }
    }

    void LoadBounds()
    {
        Rectangle area = Screen.PrimaryScreen.WorkingArea;
        Size size = new Size(Math.Min(1320, area.Width - 40), Math.Min(880, area.Height - 40));
        Bounds = new Rectangle(area.Left + (area.Width - size.Width) / 2, area.Top + (area.Height - size.Height) / 2, size.Width, size.Height);
        try
        {
            if (!File.Exists(boundsFile)) return;
            string[] p = File.ReadAllText(boundsFile).Trim().Split(',');
            Rectangle r = new Rectangle(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
            foreach (Screen s in Screen.AllScreens)
            {
                if (s.WorkingArea.IntersectsWith(r) && r.Width >= 400 && r.Height >= 300)
                {
                    Bounds = r;
                    if (p.Length > 4 && p[4] == "1") WindowState = FormWindowState.Maximized;
                    break;
                }
            }
        }
        catch { }
    }

    void SaveBounds()
    {
        try
        {
            if (full) SetFull(false);
            Rectangle b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            File.WriteAllText(boundsFile, b.X + "," + b.Y + "," + b.Width + "," + b.Height + "," + (WindowState == FormWindowState.Maximized ? "1" : "0"));
        }
        catch { }
    }
}
