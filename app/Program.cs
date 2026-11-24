// AutoPick LoL: aceita a fila, bane e trava campeão por prioridade pela API local do cliente (LCU).
// Janela nativa (WinForms, .NET Framework 4.x que já vem no Windows 10/11).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class J
{
    static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    public static object Ler(string s) { try { return string.IsNullOrEmpty(s) ? null : js.DeserializeObject(s); } catch { return null; } }
    public static string Escrever(object o) { return js.Serialize(o); }
    public static Dictionary<string, object> D(object o) { return o as Dictionary<string, object>; }
    public static object[] A(object o) { return o as object[] ?? new object[0]; }
    public static object G(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) ? v : null; }
    public static int I(object o) { try { return o == null ? 0 : Convert.ToInt32(o); } catch { return 0; } }
    public static bool B(object o) { return o is bool && (bool)o; }
    public static string S(object o) { return o as string ?? ""; }
    public static string Norm(string s) { return Regex.Replace((s ?? "").ToLowerInvariant(), "[^a-z0-9]", ""); }
}

class Decisao
{
    public Dictionary<string, object> Acao;
    public int Campeao; // 0 = nenhum da lista serve
    public List<KeyValuePair<int, string>> Pulados = new List<KeyValuePair<int, string>>();
    public bool Ban { get { return J.S(J.G(Acao, "type")) == "ban"; } }
}

static class Regras
{
    // null = não é sua vez. Na hora do ban, não bane o que algum aliado marcou ou declarou.
    // acao: usa esta ação em vez da que está em andamento (para marcar o pick antes da sua vez).
    public static Decisao Decidir(Dictionary<string, object> s, List<int> picks, List<int> bans, HashSet<int> disp, Dictionary<string, object> acao = null)
    {
        int eu = J.I(J.G(s, "localPlayerCellId"));
        var acoes = J.A(J.G(s, "actions")).SelectMany(J.A).Select(J.D).Where(a => a != null).ToList();
        var minha = acao ?? acoes.FirstOrDefault(a => J.I(J.G(a, "actorCellId")) == eu && J.B(J.G(a, "isInProgress")) && !J.B(J.G(a, "completed"))
            && (J.S(J.G(a, "type")) == "pick" || J.S(J.G(a, "type")) == "ban"));
        if (minha == null) return null;
        var usados = new HashSet<int>(acoes.Where(a => J.B(J.G(a, "completed"))).Select(a => J.I(J.G(a, "championId"))));
        var b = J.D(J.G(s, "bans"));
        foreach (var x in J.A(J.G(b, "myTeamBans")).Concat(J.A(J.G(b, "theirTeamBans")))) usados.Add(J.I(x));
        foreach (var p in J.A(J.G(s, "theirTeam")).Select(J.D)) usados.Add(J.I(J.G(p, "championId")));
        var aliados = new HashSet<int>();
        foreach (var p in J.A(J.G(s, "myTeam")).Select(J.D).Where(p => p != null && J.I(J.G(p, "cellId")) != eu))
        { aliados.Add(J.I(J.G(p, "championId"))); aliados.Add(J.I(J.G(p, "championPickIntent"))); }

        var d = new Decisao { Acao = minha };
        foreach (var c in d.Ban ? bans : picks)
        {
            string motivo = usados.Contains(c) ? "banido ou já escolhido"
                : !disp.Contains(c) ? (d.Ban ? "indisponível" : "você não tem ou indisponível")
                : d.Ban && aliados.Contains(c) ? "um aliado quer" : null;
            if (motivo == null) { d.Campeao = c; return d; }
            d.Pulados.Add(new KeyValuePair<int, string>(c, motivo));
        }
        return d;
    }

    // Picks da posição sorteada primeiro, depois a lista reserva ("") sem repetir.
    public static List<string> ListaPick(Dictionary<string, List<string>> picks, string pos)
    {
        var r = new List<string>(); List<string> l;
        if (!string.IsNullOrEmpty(pos) && picks.TryGetValue(pos, out l)) r.AddRange(l);
        if (picks.TryGetValue("", out l)) r.AddRange(l.Where(x => !r.Contains(x)));
        return r;
    }
}

class Lcu
{
    public string Porta, Auth;
    public int Req(string metodo, string rota, string corpo, out string texto)
    {
        var r = (HttpWebRequest)WebRequest.Create("https://127.0.0.1:" + Porta + rota);
        r.Method = metodo; r.Headers["Authorization"] = Auth; r.Accept = "application/json"; r.Timeout = 5000;
        r.ServerCertificateValidationCallback = (a, c, ch, e) => true; // certificado autoassinado do próprio cliente, só em 127.0.0.1
        if (corpo != null)
        {
            var bytes = Encoding.UTF8.GetBytes(corpo);
            r.ContentType = "application/json"; r.ContentLength = bytes.Length;
            using (var st = r.GetRequestStream()) st.Write(bytes, 0, bytes.Length);
        }
        else if (metodo != "GET") r.ContentLength = 0;
        try { using (var resp = (HttpWebResponse)r.GetResponse()) { texto = Ler(resp); return (int)resp.StatusCode; } }
        catch (WebException ex) when (ex.Response is HttpWebResponse)
        { var resp = (HttpWebResponse)ex.Response; texto = Ler(resp); return (int)resp.StatusCode; }
    }
    static string Ler(HttpWebResponse r) { using (var sr = new StreamReader(r.GetResponseStream())) return sr.ReadToEnd(); }
}

static class Cores
{
    public static readonly Color Fundo = Color.FromArgb(13, 17, 28), Cartao = Color.FromArgb(24, 30, 46), CartaoHover = Color.FromArgb(36, 46, 72),
        Texto = Color.FromArgb(236, 232, 222), Sutil = Color.FromArgb(135, 145, 168), Linha = Color.FromArgb(44, 52, 76),
        Ouro = Color.FromArgb(200, 155, 60), Azul = Color.FromArgb(10, 200, 185), Vermelho = Color.FromArgb(232, 64, 87),
        Verde = Color.FromArgb(70, 200, 110), Roxo = Color.FromArgb(140, 110, 240), Laranja = Color.FromArgb(240, 150, 50);
    public static Color Mix(Color a, Color b, float t)
    {
        t = t < 0 ? 0 : t > 1 ? 1 : t;
        return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }
    public static readonly StringFormat Centro = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
}

// Controle desenhado à mão com animações suaves (um relógio só para todos).
abstract class Animado : Control
{
    static readonly System.Windows.Forms.Timer relogio = new System.Windows.Forms.Timer { Interval = 15 };
    static readonly HashSet<Animado> ativos = new HashSet<Animado>();
    static Animado()
    {
        relogio.Tick += (o, e) =>
        {
            foreach (var a in ativos.ToList()) if (a.IsDisposed || !a.Passo()) ativos.Remove(a);
            if (ativos.Count == 0) relogio.Stop();
        };
    }
    protected float hover, hoverAlvo;
    protected Animado()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Cursor = Cursors.Hand;
    }
    protected void Animar() { if (IsDisposed) return; ativos.Add(this); relogio.Start(); }
    // true enquanto ainda tem animação rodando
    protected virtual bool Passo() { bool mexe = Aproxima(ref hover, hoverAlvo); Invalidate(); return mexe; }
    protected static bool Aproxima(ref float v, float alvo)
    {
        if (Math.Abs(alvo - v) < 0.02f) { v = alvo; return false; }
        v += (alvo - v) * 0.22f; return true;
    }
    protected override void OnMouseEnter(EventArgs e) { hoverAlvo = 1; Animar(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hoverAlvo = 0; Animar(); base.OnMouseLeave(e); }
    // A roda do mouse em cima de um cartão rola a grade.
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var p = Parent as ScrollableControl;
        if (p != null && p.AutoScroll) p.AutoScrollPosition = new Point(0, Math.Max(0, -p.AutoScrollPosition.Y - e.Delta));
        base.OnMouseWheel(e);
    }
    protected Graphics Preparar(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.Clear(Parent != null ? Parent.BackColor : Cores.Fundo);
        return g;
    }
    public static System.Drawing.Drawing2D.GraphicsPath Redondo(RectangleF r, float raio)
    {
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        float d = Math.Min(raio * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
    protected static void Imagem(Graphics g, Image img, RectangleF r, float raio, float alfa)
    {
        using (var p = Redondo(r, raio))
        {
            var antes = g.Clip; g.SetClip(p, System.Drawing.Drawing2D.CombineMode.Intersect);
            if (alfa >= 0.99f) g.DrawImage(img, r);
            else using (var at = new System.Drawing.Imaging.ImageAttributes())
                {
                    at.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = alfa });
                    g.DrawImage(img, new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, at);
                }
            g.Clip = antes;
        }
    }
}

class Chip : Animado
{
    bool sel; float s;
    public Color Cor;
    public bool Selecionado { get { return sel; } set { if (sel == value) return; sel = value; Animar(); } }
    public Chip(string texto, Color cor)
    {
        Text = texto; Cor = cor; Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        Size = new Size(TextRenderer.MeasureText(texto, Font).Width + 30, 32); Margin = new Padding(3);
    }
    protected override bool Passo() { return base.Passo() | Aproxima(ref s, sel ? 1 : 0); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Preparar(e);
        var r = new RectangleF(1, 1, Width - 3, Height - 3);
        using (var p = Redondo(r, r.Height / 2))
        {
            using (var b = new SolidBrush(Cores.Mix(Cores.Mix(Cores.Cartao, Cores.CartaoHover, hover), Cor, s))) g.FillPath(b, p);
            using (var pen = new Pen(Cores.Mix(Cores.Linha, Cor, Math.Max(s, hover * 0.7f)), 1.5f)) g.DrawPath(pen, p);
        }
        using (var b = new SolidBrush(Cores.Mix(Cores.Texto, Cores.Fundo, s))) g.DrawString(Text, Font, b, r, Cores.Centro);
    }
}

class Cartao : Animado
{
    public string Id, Nome; public string[] Tags; public int Key;
    bool temRunas; public bool TemRunas { get { return temRunas; } set { if (temRunas != value) { temRunas = value; Invalidate(); } } }
    Image icone; float aparece, marcado, pop;
    public string Selo; Color corSelo = Cores.Azul;
    public Image Icone { get { return icone; } set { icone = value; aparece = 0; Animar(); } }
    public Cartao() { Size = new Size(96, 118); Margin = new Padding(5); Font = new Font("Segoe UI", 8.5f); }
    public void Marcar(string selo, Color cor)
    {
        if (selo == Selo && cor == corSelo) return;
        if (selo != null) pop = 1;
        Selo = selo; if (selo != null) corSelo = cor; Animar();
    }
    protected override bool Passo()
    {
        bool a = base.Passo();
        a |= Aproxima(ref aparece, icone == null ? 0 : 1);
        a |= Aproxima(ref marcado, Selo == null ? 0 : 1);
        a |= Aproxima(ref pop, 0);
        return a;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Preparar(e);
        float sobe = hover * 3;
        var r = new RectangleF(3, 4 - sobe, Width - 7, Height - 9);
        using (var p = Redondo(r, 12))
        {
            using (var b = new SolidBrush(Cores.Mix(Cores.Mix(Cores.Cartao, Cores.CartaoHover, hover), corSelo, marcado * 0.18f))) g.FillPath(b, p);
            var borda = marcado > 0.01f ? Cores.Mix(Cores.Linha, corSelo, marcado) : Cores.Mix(Cores.Linha, Cores.Ouro, hover * 0.6f);
            using (var pen = new Pen(borda, 1 + marcado * 1.5f)) g.DrawPath(pen, p);
        }
        float tam = 64 + pop * 8;
        var ir = new RectangleF((Width - tam) / 2, 12 - sobe - pop * 4, tam, tam);
        if (icone != null) Imagem(g, icone, ir, 10, aparece);
        else using (var p = Redondo(ir, 10)) using (var b = new SolidBrush(Cores.Linha)) g.FillPath(b, p);
        using (var b = new SolidBrush(Cores.Mix(Cores.Sutil, Cores.Texto, Math.Max(hover, marcado))))
            g.DrawString(Nome, Font, b, new RectangleF(5, 80 - sobe, Width - 10, 28), Cores.Centro);
        if (temRunas)
        {
            var rr = new RectangleF(ir.X - 4, ir.Bottom - 16, 20, 20);
            using (var b = new SolidBrush(Cores.Roxo)) g.FillEllipse(b, rr);
            using (var f = new Font("Segoe UI", 7.5f, FontStyle.Bold)) using (var b = new SolidBrush(Cores.Texto)) g.DrawString("R", f, b, rr, Cores.Centro);
        }
        if (marcado > 0.01f && Selo != null)
        {
            float d = 24 + pop * 8;
            var c = new RectangleF(Width - 12 - d / 2 - 6, 2 - sobe, d, d);
            using (var b = new SolidBrush(Color.FromArgb((int)(255 * marcado), corSelo))) g.FillEllipse(b, c);
            using (var f = new Font("Segoe UI", 8.5f, FontStyle.Bold)) using (var b = new SolidBrush(Color.FromArgb((int)(255 * marcado), Cores.Fundo)))
                g.DrawString(Selo, f, b, c, Cores.Centro);
        }
    }
}

class Vaga : Animado
{
    public int Numero; public string Id, Nome; Image icone; public Color Cor = Cores.Azul; float pop;
    public Vaga(int n) { Numero = n; Size = new Size(68, 68); Margin = new Padding(4); Font = new Font("Segoe UI", 16f, FontStyle.Bold); }
    public void Definir(string id, string nome, Image ic, Color cor)
    {
        if (id != null && id != Id) pop = 1;
        Id = id; Nome = nome; icone = ic; Cor = cor; Animar();
    }
    protected override bool Passo() { return base.Passo() | Aproxima(ref pop, 0); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Preparar(e);
        var r = new RectangleF(3, 3, Width - 7, Height - 7);
        if (Id == null)
        {
            using (var p = Redondo(r, 12)) using (var pen = new Pen(Cores.Mix(Cores.Linha, Cor, hover * 0.6f), 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                g.DrawPath(pen, p);
            using (var b = new SolidBrush(Cores.Linha)) g.DrawString(Numero.ToString(), Font, b, r, Cores.Centro);
            return;
        }
        float cresce = pop * 5;
        var ir = new RectangleF(r.X - cresce / 2, r.Y - cresce / 2, r.Width + cresce, r.Height + cresce);
        if (icone != null) Imagem(g, icone, ir, 12, 1);
        else using (var p = Redondo(ir, 12)) using (var b = new SolidBrush(Cores.Cartao)) { g.FillPath(b, p); using (var f = new Font("Segoe UI", 7.5f)) using (var t = new SolidBrush(Cores.Texto)) g.DrawString(Nome, f, t, ir, Cores.Centro); }
        using (var p = Redondo(ir, 12)) using (var pen = new Pen(Cor, 2)) g.DrawPath(pen, p);
        if (hover > 0.01f)
        {
            using (var p = Redondo(ir, 12)) using (var b = new SolidBrush(Color.FromArgb((int)(170 * hover), 0, 0, 0))) g.FillPath(b, p);
            using (var f = new Font("Segoe UI", 8f, FontStyle.Bold)) using (var b = new SolidBrush(Color.FromArgb((int)(255 * hover), Cores.Texto))) g.DrawString("Tirar", f, b, ir, Cores.Centro);
        }
        var c = new RectangleF(0, 0, 20, 20);
        using (var b = new SolidBrush(Cor)) g.FillEllipse(b, c);
        using (var f = new Font("Segoe UI", 8f, FontStyle.Bold)) using (var b = new SolidBrush(Cores.Fundo)) g.DrawString(Numero.ToString(), f, b, c, Cores.Centro);
    }
}

// Faixa de status no topo: muda de cor suavemente e pulsa enquanto espera algo.
class Faixa : Animado
{
    string texto = "Iniciando...", sub = "";
    Color cor = Cores.Sutil, corAlvo = Cores.Sutil;
    float pulso, mistura = 1; bool pulsando;
    public Faixa() { Cursor = Cursors.Default; Height = 64; }
    protected override void OnMouseEnter(EventArgs e) { }
    protected override void OnMouseLeave(EventArgs e) { }
    public void Mostrar(string t, string subtitulo, Color c, bool pulsar)
    {
        texto = t; sub = subtitulo ?? "";
        if (c != corAlvo) { cor = Cores.Mix(cor, corAlvo, mistura); corAlvo = c; mistura = 0; }
        pulsando = pulsar; Animar();
    }
    protected override bool Passo()
    {
        bool a = Aproxima(ref mistura, 1);
        if (pulsando) { pulso += 0.06f; a = true; }
        Invalidate(); return a;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Preparar(e);
        var c = Cores.Mix(cor, corAlvo, mistura);
        using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, Width, Height), Color.FromArgb(60, c), Cores.Fundo, 0f)) g.FillRectangle(b, 0, 0, Width, Height);
        using (var b = new SolidBrush(c)) g.FillRectangle(b, 0, 0, 6, Height);
        float raio = 7 + (pulsando ? (float)Math.Sin(pulso) * 2.5f : 0);
        if (pulsando) using (var b = new SolidBrush(Color.FromArgb((int)(60 + 50 * Math.Sin(pulso)), c))) g.FillEllipse(b, 30 - raio * 1.8f, Height / 2f - raio * 1.8f, raio * 3.6f, raio * 3.6f);
        using (var b = new SolidBrush(c)) g.FillEllipse(b, 30 - raio, Height / 2f - raio, raio * 2, raio * 2);
        using (var f = new Font("Segoe UI Semibold", 14f)) using (var b = new SolidBrush(Cores.Texto))
            g.DrawString(texto, f, b, new PointF(52, sub == "" ? 18 : 8));
        if (sub != "") using (var f = new Font("Segoe UI", 9f)) using (var b = new SolidBrush(Cores.Sutil)) g.DrawString(sub, f, b, new PointF(54, 38));
    }
}

class Runa { public int Id; public string Nome, Desc, Icone; }
class Estilo { public int Id; public string Nome, Icone; public List<List<Runa>> Linhas = new List<List<Runa>>(); }
class Fragmento { public int Id; public string Nome; }

// Página de runas: 4 da principal, 2 da secundária, 3 fragmentos (mesma ordem que o cliente usa).
class Pagina
{
    public int Primaria, Secundaria; public List<int> Perks = new List<int>();
    public bool Completa { get { return Primaria > 0 && Secundaria > 0 && Perks.Count == 9 && Perks.All(p => p > 0); } }
    public Dictionary<string, object> ParaJson() { return new Dictionary<string, object> { { "primaria", Primaria }, { "secundaria", Secundaria }, { "perks", Perks } }; }
    public static Pagina De(object o)
    {
        var d = J.D(o); if (d == null) return null;
        var p = new Pagina { Primaria = J.I(J.G(d, "primaria")), Secundaria = J.I(J.G(d, "secundaria")), Perks = J.A(J.G(d, "perks")).Select(J.I).ToList() };
        return p.Completa ? p : null;
    }
}

static class DadosRunas
{
    // Árvores e runas vêm do Data Dragon (atualiza a cada patch). Fragmentos: padrão abaixo, trocados pelos do cliente quando conecta.
    public static List<Estilo> Estilos = new List<Estilo>();
    public static List<List<Fragmento>> Fragmentos = new List<List<Fragmento>> {
        F(5008, "Força Adaptativa", 5005, "Velocidade de Ataque", 5007, "Aceleração de Habilidade"),
        F(5008, "Força Adaptativa", 5010, "Velocidade de Movimento", 5001, "Vida Escalável"),
        F(5011, "Vida", 5013, "Tenacidade e Resist. a Lentidão", 5001, "Vida Escalável") };
    static List<Fragmento> F(int a, string na, int b, string nb, int c, string nc)
    { return new List<Fragmento> { new Fragmento { Id = a, Nome = na }, new Fragmento { Id = b, Nome = nb }, new Fragmento { Id = c, Nome = nc } }; }
    public static readonly Dictionary<string, Image> Imagens = new Dictionary<string, Image>(); // só na thread da janela
    public static event Action Mudou;
    public static void Avisar() { var m = Mudou; if (m != null) m(); }
    public static Estilo EstiloDe(int id) { return Estilos.FirstOrDefault(e => e.Id == id); }
    public static Runa Achar(int id) { return Estilos.SelectMany(e => e.Linhas).SelectMany(l => l).FirstOrDefault(r => r.Id == id); }
    public static string NomeFragmento(int id) { var f = Fragmentos.SelectMany(x => x).FirstOrDefault(x => x.Id == id); return f != null ? f.Nome : id.ToString(); }
    // Secundária: no máximo 2 runas, de linhas diferentes; a mais antiga sai (igual ao cliente).
    public static List<KeyValuePair<int, int>> Secundaria(List<KeyValuePair<int, int>> sel, int linha, int runa)
    {
        var r = sel.Where(x => x.Key != linha).ToList();
        r.Add(new KeyValuePair<int, int>(linha, runa));
        while (r.Count > 2) r.RemoveAt(0);
        return r;
    }
}

class BotaoRuna : Animado
{
    public string Icone, TextoAlt; bool sel, apagado;
    public BotaoRuna(string icone, int tam) { Icone = icone; Size = new Size(tam, tam); Margin = new Padding(0); }
    public void Marcar(bool s, bool a) { sel = s; apagado = a; Animar(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Preparar(e);
        var r = new RectangleF(3, 3, Width - 7, Height - 7);
        Image img; DadosRunas.Imagens.TryGetValue(Icone ?? "", out img);
        using (var b = new SolidBrush(Cores.Mix(sel ? Color.FromArgb(40, 34, 20) : Cores.Cartao, Cores.CartaoHover, hover))) g.FillEllipse(b, r);
        if (img != null) Imagem(g, img, new RectangleF(r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8), r.Width, sel || !apagado ? 1f : 0.28f + hover * 0.45f);
        else if (TextoAlt != null) using (var f = new Font("Segoe UI", 7f)) using (var b = new SolidBrush(Cores.Texto)) g.DrawString(TextoAlt, f, b, r, Cores.Centro);
        using (var pen = new Pen(sel ? Cores.Ouro : Cores.Mix(Cores.Linha, Cores.Ouro, hover * 0.7f), sel ? 2.5f : 1.5f)) g.DrawEllipse(pen, r);
    }
}

class PaginaStat : Pagina { public int Jogos, Vitorias; }

// Editor de runas no estilo do cliente. Ao lado: as mais usadas no BR (seu servidor) e a recomendação do próprio LoL.
class RunasForm : Form
{
    readonly Func<Pagina> buscarRec;
    public Pagina Resultado;
    int prim, sec; int[] primSel = new int[4], frag = new int[3];
    List<KeyValuePair<int, int>> secSel = new List<KeyValuePair<int, int>>();
    readonly Panel area, painelStats; readonly Label recTexto, falta; readonly Button usarRec, salvar;
    readonly ToolTip dica = new ToolTip { InitialDelay = 200 };
    readonly int key; readonly string rota; string servidor; readonly Action<string> salvarServidor;
    Pagina rec;

    public RunasForm(string nome, Pagina atual, Func<Pagina> buscar, int chaveCampeao, string rotaAtual, string servidorRunas, Action<string> guardarServidor)
    {
        buscarRec = buscar; key = chaveCampeao; rota = (rotaAtual ?? "").ToUpperInvariant(); servidor = servidorRunas ?? ""; salvarServidor = guardarServidor;
        Text = "Runas de " + nome; BackColor = Cores.Fundo; ForeColor = Cores.Texto; Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(1100, 650); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; DoubleBuffered = true;

        area = new Panel { Location = new Point(0, 0), Size = new Size(770, 590) };
        var lado = new Panel { Location = new Point(780, 12), Size = new Size(306, 580), BackColor = Cores.Cartao };
        lado.Controls.Add(new Label { Text = "Mais usadas no BR", Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), ForeColor = Cores.Ouro, Location = new Point(10, 8), AutoSize = true });
        lado.Controls.Add(new Label { Text = (rota == "" ? "Todas as rotas" : Janela.NomePos(rota.ToLowerInvariant())) + " · Esmeralda+", ForeColor = Cores.Sutil, Location = new Point(11, 30), AutoSize = true });
        painelStats = new Panel { Location = new Point(0, 52), Size = new Size(306, 318) };
        lado.Controls.Add(painelStats);
        lado.Controls.Add(new Label { Text = "Recomendada pelo LoL", Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), ForeColor = Cores.Ouro, Location = new Point(10, 378), AutoSize = true });
        recTexto = new Label { Location = new Point(10, 402), Size = new Size(286, 44), ForeColor = Cores.Texto, Text = "Buscando no cliente..." };
        usarRec = Botao("Usar esta", Cores.Roxo, new Point(10, 450), 286); usarRec.Enabled = false;
        usarRec.Click += (o, e) => { if (rec != null) Carregar(rec); };
        lado.Controls.Add(recTexto); lado.Controls.Add(usarRec);
        lado.Controls.Add(new Label { Text = "% de vitória: partidas Solo/Duo do BR coletadas pelo seu servidor (patch atual e anterior). A recomendação do LoL não informa vitórias.", Location = new Point(10, 496), Size = new Size(286, 80), ForeColor = Cores.Sutil, Font = new Font("Segoe UI", 8f) });

        salvar = Botao("Salvar runas", Cores.Verde, new Point(20, 600), 160);
        salvar.Click += (o, e) => { Resultado = Montada(); DialogResult = DialogResult.OK; };
        var auto = Botao("Usar recomendação automática", Cores.CartaoHover, new Point(190, 600), 230);
        auto.Click += (o, e) => { Resultado = null; DialogResult = DialogResult.OK; };
        var cancelar = Botao("Cancelar", Cores.CartaoHover, new Point(430, 600), 110);
        cancelar.Click += (o, e) => { DialogResult = DialogResult.Cancel; };
        falta = new Label { Location = new Point(560, 604), Size = new Size(210, 40), ForeColor = Cores.Sutil };
        Controls.Add(area); Controls.Add(lado); Controls.Add(salvar); Controls.Add(auto); Controls.Add(cancelar); Controls.Add(falta);

        Action redesenhar = () => { foreach (Control c in area.Controls) c.Invalidate(); };
        DadosRunas.Mudou += redesenhar; FormClosed += (o, e) => DadosRunas.Mudou -= redesenhar;
        if (atual != null) Carregar(atual);
        else { prim = DadosRunas.Estilos.Count > 0 ? DadosRunas.Estilos[0].Id : 0; Montar(); }
        Shown += (o, e) =>
        {
            Em2oPlano(() => { try { return buscarRec(); } catch { return null; } }, MostrarRec);
            BuscarStats();
        };
    }

    void Em2oPlano<T>(Func<T> trabalho, Action<T> depois)
    {
        new Thread(() => { var r = trabalho(); try { BeginInvoke((Action)(() => { if (!IsDisposed) depois(r); })); } catch { } }) { IsBackground = true }.Start();
    }

    Label Texto(Control pai, string t, int x, int y, int w, int h, Color cor, float tam = 9f, FontStyle estilo = FontStyle.Regular)
    { var l = new Label { Text = t, Location = new Point(x, y), Size = new Size(w, h), ForeColor = cor, Font = new Font("Segoe UI", tam, estilo) }; pai.Controls.Add(l); return l; }

    void BuscarStats()
    {
        foreach (Control c in painelStats.Controls.Cast<Control>().ToList()) c.Dispose();
        painelStats.Controls.Clear();
        if (servidor == "")
        {
            Texto(painelStats, "Cole o endereço do seu servidor de runas (o Worker do Cloudflare):", 10, 4, 286, 36, Cores.Sutil);
            var caixa = new TextBox { Location = new Point(10, 44), Width = 286, BackColor = Cores.Fundo, ForeColor = Cores.Texto, BorderStyle = BorderStyle.FixedSingle };
            var ok = Botao("Salvar endereço", Cores.Ouro, new Point(10, 76), 286);
            ok.Click += (o, e) =>
            {
                var t = caixa.Text.Trim().TrimEnd('/');
                if (!t.StartsWith("https://")) { MessageBox.Show(this, "O endereço começa com https://", "AutoPick"); return; }
                servidor = t; salvarServidor(t); BuscarStats();
            };
            painelStats.Controls.Add(caixa); painelStats.Controls.Add(ok);
            return;
        }
        var carregando = Texto(painelStats, "Buscando no servidor...", 10, 4, 286, 20, Cores.Sutil);
        var url = servidor + "/runas?campeao=" + key + (rota == "" ? "" : "&rota=" + rota);
        Em2oPlano(() =>
        {
            try { using (var w = new WebClient { Encoding = Encoding.UTF8 }) return J.D(J.Ler(w.DownloadString(url))); }
            catch (Exception e) { return new Dictionary<string, object> { { "falha", e.Message } }; }
        }, d => MostrarStats(d, carregando));
    }

    void MostrarStats(Dictionary<string, object> d, Label carregando)
    {
        carregando.Dispose();
        var br = new System.Globalization.CultureInfo("pt-BR");
        if (d == null || J.G(d, "falha") != null)
        {
            Texto(painelStats, "Não consegui falar com o servidor" + (d == null ? "." : ": " + J.S(J.G(d, "falha"))), 10, 4, 286, 60, Cores.Laranja);
            var trocar = Botao("Trocar endereço", Cores.CartaoHover, new Point(10, 70), 286);
            trocar.Click += (o, e) => { servidor = ""; BuscarStats(); };
            painelStats.Controls.Add(trocar);
            return;
        }
        int total = J.I(J.G(d, "total"));
        var lista = J.A(J.G(d, "paginas")).Select(J.D).Where(x => x != null).Select(x => new PaginaStat
        {
            Primaria = J.I(J.G(x, "primaria")), Secundaria = J.I(J.G(x, "secundaria")), Perks = J.A(J.G(x, "perks")).Select(J.I).ToList(),
            Jogos = J.I(J.G(x, "jogos")), Vitorias = J.I(J.G(x, "vitorias"))
        }).Where(p => p.Completa).ToList();
        if (lista.Count == 0) { Texto(painelStats, "Ainda sem partidas suficientes desse campeão" + (rota == "" ? "" : " nessa rota") + ". O servidor coleta o dia todo; volte mais tarde.", 10, 4, 286, 60, Cores.Sutil); return; }
        int y = 2;
        foreach (var p in lista)
        {
            var r = DadosRunas.Achar(p.Perks[0]); var ep = DadosRunas.EstiloDe(p.Primaria); var es = DadosRunas.EstiloDe(p.Secundaria);
            double taxa = p.Jogos > 0 ? (double)p.Vitorias / p.Jogos : 0;
            var caixa = new Panel { Location = new Point(8, y), Size = new Size(290, 98), BackColor = Cores.CartaoHover };
            Texto(caixa, "★ " + (r != null ? r.Nome : "?"), 8, 6, 200, 20, Cores.Texto, 9.5f, FontStyle.Bold);
            Texto(caixa, (ep != null ? ep.Nome : "?") + " + " + (es != null ? es.Nome : "?"), 8, 27, 270, 18, Cores.Sutil);
            Texto(caixa, (taxa * 100).ToString("0.0", br) + "% de vitória", 8, 48, 180, 20, taxa >= 0.5 ? Cores.Verde : Cores.Laranja, 10f, FontStyle.Bold);
            Texto(caixa, p.Jogos.ToString("N0", br) + " jogos · " + (total > 0 ? ((double)p.Jogos / total * 100).ToString("0", br) + "% das partidas" : ""), 8, 72, 200, 18, Cores.Sutil, 8f);
            var usar = Botao("Usar", Cores.Roxo, new Point(214, 52), 68); usar.Height = 32;
            var pg = p; usar.Click += (o, e) => Carregar(pg);
            caixa.Controls.Add(usar);
            var nomes = string.Join("\n", pg.Perks.Take(6).Select(id => { var x = DadosRunas.Achar(id); return x != null ? x.Nome : id.ToString(); }).Concat(pg.Perks.Skip(6).Select(DadosRunas.NomeFragmento)));
            dica.SetToolTip(caixa, nomes); foreach (Control c in caixa.Controls) dica.SetToolTip(c, nomes);
            painelStats.Controls.Add(caixa); y += 104;
        }
    }

    static Button Botao(string t, Color cor, Point onde, int largura)
    {
        var b = new Button { Text = t, Location = onde, Size = new Size(largura, 36), FlatStyle = FlatStyle.Flat, BackColor = cor, ForeColor = Cores.Texto, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand };
        b.FlatAppearance.BorderSize = 0; return b;
    }

    void MostrarRec(Pagina p)
    {
        rec = p;
        if (p == null) { recTexto.Text = "Abra o cliente do LoL para ver a recomendação dele."; return; }
        var r = DadosRunas.Achar(p.Perks.FirstOrDefault()); var ep = DadosRunas.EstiloDe(p.Primaria); var es = DadosRunas.EstiloDe(p.Secundaria);
        recTexto.Text = "★ " + (r != null ? r.Nome : "?") + "\n" + (ep != null ? ep.Nome : "?") + " + " + (es != null ? es.Nome : "?");
        dica.SetToolTip(recTexto, string.Join("\n", p.Perks.Take(6).Select(id => { var x = DadosRunas.Achar(id); return x != null ? x.Nome : id.ToString(); }).Concat(p.Perks.Skip(6).Select(DadosRunas.NomeFragmento))));
        usarRec.Enabled = true;
    }

    void Carregar(Pagina p)
    {
        prim = p.Primaria; sec = p.Secundaria;
        var ep = DadosRunas.EstiloDe(prim); var es = DadosRunas.EstiloDe(sec);
        primSel = new int[4]; secSel.Clear(); frag = new int[3];
        if (ep != null) for (int i = 0; i < 4 && i < ep.Linhas.Count; i++) primSel[i] = ep.Linhas[i].Select(r => r.Id).FirstOrDefault(p.Perks.Contains);
        if (es != null) for (int i = 1; i < es.Linhas.Count; i++) { int id = es.Linhas[i].Select(r => r.Id).FirstOrDefault(p.Perks.Contains); if (id > 0 && secSel.Count < 2) secSel.Add(new KeyValuePair<int, int>(i, id)); }
        for (int i = 0; i < 3 && 6 + i < p.Perks.Count; i++) frag[i] = p.Perks[6 + i];
        Montar();
    }

    Pagina Montada()
    {
        var p = new Pagina { Primaria = prim, Secundaria = sec };
        p.Perks.AddRange(primSel); p.Perks.AddRange(secSel.OrderBy(x => x.Key).Select(x => x.Value)); p.Perks.AddRange(frag);
        return p;
    }

    Label Titulo(string t, int x, int y) { var l = new Label { Text = t, Location = new Point(x, y), AutoSize = true, ForeColor = Cores.Ouro, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) }; area.Controls.Add(l); return l; }

    BotaoRuna Runa(Runa r, int x, int y, int tam, bool marcado, bool algumNaLinha, Action clique)
    {
        var b = new BotaoRuna(r.Icone, tam) { Location = new Point(x, y), TextoAlt = r.Nome };
        b.Marcar(marcado, algumNaLinha && !marcado);
        b.Click += (o, e) => clique();
        dica.SetToolTip(b, r.Nome + (string.IsNullOrEmpty(r.Desc) ? "" : "\n" + r.Desc));
        area.Controls.Add(b); return b;
    }

    void Montar()
    {
        area.SuspendLayout();
        foreach (Control c in area.Controls.Cast<Control>().ToList()) c.Dispose();
        area.Controls.Clear();
        if (DadosRunas.Estilos.Count == 0) { Titulo("As runas ainda estão carregando. Feche e abra de novo em alguns segundos.", 20, 20); area.ResumeLayout(); return; }

        // Árvore principal
        Titulo("Árvore principal", 20, 14);
        int x = 20;
        foreach (var est in DadosRunas.Estilos)
        {
            var id = est.Id;
            var b = new BotaoRuna(est.Icone, 50) { Location = new Point(x, 40), TextoAlt = est.Nome };
            b.Marcar(id == prim, prim != 0 && id != prim);
            b.Click += (o, e) => { if (prim == id) return; prim = id; primSel = new int[4]; if (sec == prim) { sec = 0; secSel.Clear(); } Montar(); };
            dica.SetToolTip(b, est.Nome); area.Controls.Add(b); x += 60;
        }
        var ep = DadosRunas.EstiloDe(prim);
        if (ep != null)
        {
            for (int i = 0; i < ep.Linhas.Count && i < 4; i++)
            {
                int linha = i, tam = i == 0 ? 64 : 48, y = i == 0 ? 106 : 186 + (i - 1) * 66;
                int xi = 20 + (i == 0 ? 0 : 8);
                foreach (var r in ep.Linhas[i])
                {
                    var rid = r.Id;
                    Runa(r, xi, y, tam, primSel[linha] == rid, primSel[linha] != 0, () => { primSel[linha] = rid; Montar(); });
                    xi += tam + (i == 0 ? 14 : 22);
                }
            }
        }

        // Árvore secundária
        Titulo("Árvore secundária", 410, 14);
        x = 410;
        foreach (var est in DadosRunas.Estilos.Where(e => e.Id != prim))
        {
            var id = est.Id;
            var b = new BotaoRuna(est.Icone, 44) { Location = new Point(x, 40), TextoAlt = est.Nome };
            b.Marcar(id == sec, sec != 0 && id != sec);
            b.Click += (o, e) => { if (sec == id) return; sec = id; secSel.Clear(); Montar(); };
            dica.SetToolTip(b, est.Nome); area.Controls.Add(b); x += 54;
        }
        var es = DadosRunas.EstiloDe(sec);
        if (es != null)
            for (int i = 1; i < es.Linhas.Count && i < 4; i++)
            {
                int linha = i, xi = 418;
                foreach (var r in es.Linhas[i])
                {
                    var rid = r.Id; bool marcado = secSel.Any(s => s.Value == rid);
                    Runa(r, xi, 100 + (i - 1) * 56, 44, marcado, secSel.Count == 2 && !marcado, () => { secSel = DadosRunas.Secundaria(secSel, linha, rid); Montar(); });
                    xi += 60;
                }
            }
        else Titulo("← escolha a árvore secundária", 410, 110).ForeColor = Cores.Sutil;

        // Fragmentos
        Titulo("Fragmentos", 410, 290);
        for (int i = 0; i < DadosRunas.Fragmentos.Count && i < 3; i++)
        {
            int linha = i, xi = 410;
            foreach (var f in DadosRunas.Fragmentos[i])
            {
                var fid = f.Id;
                // largura fixa: nomes longos quebram em duas linhas
                var c = new Chip(f.Nome, Cores.Ouro) { Location = new Point(xi, 318 + i * 46), Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Size = new Size(114, 40) };
                dica.SetToolTip(c, f.Nome);
                c.Selecionado = frag[linha] == fid;
                c.Click += (o, e) => { frag[linha] = fid; Montar(); };
                area.Controls.Add(c); xi += c.Width + 6;
            }
        }
        area.ResumeLayout();

        var faltam = new List<string>();
        if (primSel[0] == 0) faltam.Add("pedra angular");
        if (primSel.Skip(1).Any(v => v == 0)) faltam.Add("runas principais");
        if (secSel.Count < 2) faltam.Add("2 secundárias");
        if (frag.Any(v => v == 0)) faltam.Add("fragmentos");
        salvar.Enabled = faltam.Count == 0;
        falta.Text = faltam.Count == 0 ? "Página completa!" : "Falta: " + string.Join(", ", faltam);
        falta.ForeColor = faltam.Count == 0 ? Cores.Verde : Cores.Sutil;
    }
}

class PainelSuave : FlowLayoutPanel { public PainelSuave() { DoubleBuffered = true; } }

class Janela : Form
{
    static readonly string Pasta = Path.GetDirectoryName(Application.ExecutablePath);
    static readonly string ArqCfg = Path.Combine(Pasta, "config.json");
    // chave da posição no cliente → nome mostrado. "" = lista reserva para qualquer posição.
    public static readonly string[][] Posicoes = {
        new[] { "", "Qualquer" }, new[] { "top", "Topo" }, new[] { "jungle", "Caçador" },
        new[] { "middle", "Meio" }, new[] { "bottom", "Atirador" }, new[] { "utility", "Suporte" } };
    static readonly string[][] Classes = {
        new[] { "", "Todas" }, new[] { "Fighter", "Lutador" }, new[] { "Mage", "Mago" }, new[] { "Assassin", "Assassino" },
        new[] { "Tank", "Tanque" }, new[] { "Marksman", "Atirador" }, new[] { "Support", "Suporte" } };

    readonly object trava = new object();
    string pastaLol = "C:/Riot Games/League of Legends", servidorRunas = "https://lolautopick.gui-gon96.workers.dev";
    bool aceitar = true, runas = true;
    Dictionary<string, List<string>> picks = Posicoes.ToDictionary(p => p[0], p => new List<string>());
    Dictionary<string, List<string>> bans = Posicoes.ToDictionary(p => p[0], p => new List<string>());
    Dictionary<string, Pagina> runasCamp = new Dictionary<string, Pagina>(); // id do Data Dragon → página salva
    Dictionary<int, string> ddPorKey = new Dictionary<int, string>();
    volatile Lcu conAtual; volatile string posAtual = "";
    Dictionary<string, int> idsLcu = new Dictionary<string, int>();
    Dictionary<int, string> nomesLcu = new Dictionary<int, string>();
    readonly Dictionary<string, Image> icones = new Dictionary<string, Image>();
    List<Cartao> cartoes = new List<Cartao>();

    string tipo = "picks"; // "picks", "bans" ou "runas"
    string alvo = "";      // rota sendo editada ("" = Qualquer)
    string classe = "";    // filtro de classe
    Faixa faixa;
    readonly Vaga[] vagas = new Vaga[5];
    readonly Dictionary<string, Chip> chipsTipo = new Dictionary<string, Chip>(), chipsAlvo = new Dictionary<string, Chip>(), chipsClasse = new Dictionary<string, Chip>();
    Chip chipAceitar, chipRunas;
    Label dica, rotuloRota;
    TextBox busca, log;
    PainelSuave grade;

    public Janela()
    {
        Text = "AutoPick LoL"; BackColor = Cores.Fundo; ForeColor = Cores.Texto; Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(1180, 820); MinimumSize = new Size(1000, 600); StartPosition = FormStartPosition.CenterScreen;
        Opacity = 0; DoubleBuffered = true;

        faixa = new Faixa { Dock = DockStyle.Top };

        var editor = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(14, 10, 14, 2) };
        var linha1 = Linha();
        linha1.Controls.Add(Rotulo("Lista:"));
        foreach (var t in new[] { new[] { "picks", "Picks" }, new[] { "bans", "Bans" }, new[] { "runas", "Runas" } })
        {
            var chave = t[0]; var c = new Chip(t[1], chave == "picks" ? Cores.Azul : chave == "bans" ? Cores.Vermelho : Cores.Roxo);
            c.Click += (o, e) => MudarTipo(chave); chipsTipo[chave] = c; linha1.Controls.Add(c);
        }
        rotuloRota = Rotulo("   Rota:"); linha1.Controls.Add(rotuloRota);
        foreach (var p in Posicoes)
        {
            var chave = p[0]; var c = new Chip(p[1], Cores.Ouro);
            c.Click += (o, e) => MudarAlvo(chave); chipsAlvo[chave] = c; linha1.Controls.Add(c);
        }
        var linha2 = Linha();
        for (int i = 0; i < 5; i++)
        {
            var v = vagas[i] = new Vaga(i + 1); int n = i;
            v.Click += (o, e) => { lock (trava) { var l = ListaAlvo(); if (n < l.Count) l.RemoveAt(n); else return; } Salvar(); Desenhar(); };
            linha2.Controls.Add(v);
        }
        dica = new Label { AutoSize = false, Size = new Size(470, 68), ForeColor = Cores.Sutil, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(14, 4, 4, 4), Font = new Font("Segoe UI", 9.5f) };
        linha2.Controls.Add(dica);
        var linha3 = Linha();
        chipAceitar = new Chip("Aceitar partida", Cores.Verde); chipRunas = new Chip("Runas automáticas", Cores.Roxo);
        chipAceitar.Click += (o, e) => { lock (trava) aceitar = !aceitar; chipAceitar.Selecionado = aceitar; Salvar(); };
        chipRunas.Click += (o, e) => { lock (trava) runas = !runas; chipRunas.Selecionado = runas; Salvar(); };
        linha3.Controls.Add(chipAceitar); linha3.Controls.Add(chipRunas);
        editor.Controls.Add(linha1); editor.Controls.Add(linha2); editor.Controls.Add(linha3);

        var filtros = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(14, 8, 14, 4), BackColor = Color.FromArgb(17, 22, 36) };
        busca = new TextBox { Width = 190, BackColor = Cores.Cartao, ForeColor = Cores.Texto, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 11f), Margin = new Padding(3, 5, 12, 3) };
        busca.TextChanged += (o, e) => Filtrar();
        filtros.Controls.Add(Rotulo("Buscar:")); filtros.Controls.Add(busca);
        foreach (var c in Classes)
        {
            var chave = c[0]; var ch = new Chip(c[1], Cores.Ouro);
            ch.Click += (o, e) => { classe = chave; foreach (var kv in chipsClasse) kv.Value.Selecionado = kv.Key == classe; Filtrar(); };
            chipsClasse[chave] = ch; filtros.Controls.Add(ch);
        }
        chipsClasse[""].Selecionado = true;

        grade = new PainelSuave { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10), BackColor = Cores.Fundo };
        log = new TextBox { Dock = DockStyle.Bottom, Height = 84, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(8, 11, 19), ForeColor = Cores.Sutil, Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.None };
        Controls.Add(grade); Controls.Add(log); Controls.Add(filtros); Controls.Add(editor); Controls.Add(faixa);

        CarregarConfig();
        chipAceitar.Selecionado = aceitar; chipRunas.Selecionado = runas;
        MudarTipo("picks"); MudarAlvo("");
        faixa.Mostrar("Bem-vindo ao AutoPick!", "Escolha a lista em cima e clique nos campeões na ordem de prioridade. Salva sozinho.", Cores.Ouro, false);

        Shown += (o, e) =>
        {
            var fade = new System.Windows.Forms.Timer { Interval = 15 };
            fade.Tick += (a, b) => { Opacity = Math.Min(1, Opacity + 0.08); if (Opacity >= 1) fade.Stop(); };
            fade.Start();
            new Thread(CarregarCampeoes) { IsBackground = true }.Start();
            new Thread(Laco) { IsBackground = true }.Start();
        };
    }

    static FlowLayoutPanel Linha() { return new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) }; }
    static Label Rotulo(string t) { return new Label { Text = t, AutoSize = true, ForeColor = Cores.Sutil, Margin = new Padding(3, 9, 3, 3), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) }; }
    public static string NomePos(string chave) { var p = Posicoes.FirstOrDefault(x => x[0] == chave); return p != null ? p[1] : chave; }

    List<string> ListaAlvo() { return tipo == "bans" ? bans[alvo] : picks[alvo]; }

    void MudarAlvo(string a)
    {
        alvo = a;
        foreach (var kv in chipsAlvo) kv.Value.Selecionado = kv.Key == alvo;
        AtualizarDica(); Desenhar();
    }

    void MudarTipo(string t)
    {
        tipo = t;
        foreach (var kv in chipsTipo) kv.Value.Selecionado = kv.Key == tipo;
        foreach (var v in vagas) v.Visible = tipo != "runas";
        foreach (var kv in chipsAlvo) kv.Value.Visible = tipo != "runas";
        rotuloRota.Visible = tipo != "runas";
        AtualizarDica(); Desenhar();
    }

    void AtualizarDica()
    {
        var rota = alvo == "" ? "Qualquer rota" : NomePos(alvo);
        dica.Size = new Size(tipo == "runas" ? 820 : 470, 68);
        dica.Text = tipo == "runas" ? "Clique num campeão para montar as runas dele, igual ao cliente. Campeão sem runas salvas (sem o R roxo) usa a recomendação do LoL."
            : tipo == "bans" ? "Bans de " + rota + ", em ordem. Espera 5s e pula quem um aliado quer jogar." + (alvo == "" ? " Usada quando a lista da rota acaba ou não tem rota." : " Se nenhum servir, usa Qualquer.")
            : "Picks de " + rota + ", em ordem." + (alvo == "" ? " Usada quando a rota não tem ninguém livre, ou em modos sem rota." : " Se nenhum estiver livre, usa Qualquer.");
    }

    string NomeDe(string id) { var c = cartoes.FirstOrDefault(x => x.Id == id); return c != null ? c.Nome : id; }

    void Desenhar()
    {
        List<string> l, lp, lb; lock (trava) { lp = picks[alvo].ToList(); lb = bans[alvo].ToList(); }
        l = tipo == "bans" ? lb : lp;
        var cor = tipo == "bans" ? Cores.Vermelho : Cores.Azul;
        for (int i = 0; i < 5; i++)
        {
            string id = i < l.Count ? l[i] : null; Image img = null;
            if (id != null) icones.TryGetValue(id, out img);
            vagas[i].Definir(id, id == null ? null : NomeDe(id), img, cor);
        }
        foreach (var c in cartoes)
        {
            int b = lb.IndexOf(c.Id), p = lp.IndexOf(c.Id);
            c.TemRunas = runasCamp.ContainsKey(c.Id);
            if (b >= 0) c.Marcar("B" + (b + 1), Cores.Vermelho);
            else if (p >= 0) c.Marcar((p + 1).ToString(), Cores.Azul);
            else c.Marcar(null, cor);
        }
    }

    void Filtrar()
    {
        var f = J.Norm(busca.Text);
        grade.SuspendLayout();
        foreach (var c in cartoes) c.Visible = J.Norm(c.Nome).Contains(f) && (classe == "" || c.Tags.Contains(classe));
        grade.ResumeLayout();
    }

    void Clicar(string id)
    {
        if (tipo == "runas") { AbrirRunas(id); return; }
        lock (trava)
        {
            var l = ListaAlvo();
            if (!l.Remove(id))
            {
                if (l.Count >= 5) { faixa.Mostrar("Lista cheia", "Clique num campeão lá em cima para tirar antes de pôr outro.", Cores.Laranja, false); return; }
                (tipo == "bans" ? picks[alvo] : bans[alvo]).Remove(id); // não faz sentido banir quem você quer jogar na mesma rota
                l.Add(id);
            }
        }
        Salvar(); Desenhar();
    }

    void CarregarConfig()
    {
        if (!File.Exists(ArqCfg)) { Salvar(); return; }
        var d = J.D(J.Ler(File.ReadAllText(ArqCfg)));
        if (d == null) { Log("config.json com erro: usando listas vazias. Ao mudar algo o arquivo é regravado."); return; }
        if (J.S(J.G(d, "pastaLol")) != "") pastaLol = J.S(J.G(d, "pastaLol"));
        if (J.S(J.G(d, "servidorRunas")) != "") servidorRunas = J.S(J.G(d, "servidorRunas"));
        if (J.G(d, "aceitar") is bool) aceitar = (bool)J.G(d, "aceitar");
        if (J.G(d, "runas") is bool) runas = (bool)J.G(d, "runas");
        Func<object, List<string>> lista = o => J.A(o).Select(J.S).Where(x => x != "").Take(5).ToList();
        var pk = J.G(d, "picks");
        if (pk is object[]) picks[""] = lista(pk); // config antigo: uma lista só
        else foreach (var p in Posicoes) picks[p[0]] = lista(J.G(J.D(pk), Chave(p[0])) ?? J.G(J.D(pk), p[0]));
        var bn = J.G(d, "bans");
        if (bn is object[]) bans[""] = lista(bn); // config antigo: uma lista só
        else foreach (var p in Posicoes) bans[p[0]] = lista(J.G(J.D(bn), Chave(p[0])) ?? J.G(J.D(bn), p[0]));
        var pg = J.D(J.G(d, "paginas"));
        if (pg != null) foreach (var kv in pg) { var x = Pagina.De(kv.Value); if (x != null) runasCamp[kv.Key] = x; }
    }

    // O leitor de JSON não aceita chave vazia: a lista Qualquer ("") vai para o arquivo como "qualquer".
    static string Chave(string pos) { return pos == "" ? "qualquer" : pos; }

    void Salvar()
    {
        string json;
        lock (trava) json = J.Escrever(new Dictionary<string, object> { { "pastaLol", pastaLol }, { "servidorRunas", servidorRunas }, { "aceitar", aceitar }, { "runas", runas }, { "picks", picks.ToDictionary(kv => Chave(kv.Key), kv => kv.Value) }, { "bans", bans.ToDictionary(kv => Chave(kv.Key), kv => kv.Value) },
            { "paginas", runasCamp.ToDictionary(kv => kv.Key, kv => (object)kv.Value.ParaJson()) } });
        try { File.WriteAllText(ArqCfg, json); } catch (Exception e) { Log("Não consegui salvar o config.json: " + e.Message); }
    }

    void Log(string t)
    {
        var linha = DateTime.Now.ToString("HH:mm:ss") + "  " + t;
        try { File.AppendAllText(Path.Combine(Pasta, "autopick.log"), linha + Environment.NewLine); } catch { }
        Na(() => log.AppendText(linha + Environment.NewLine));
    }
    void Na(Action a)
    {
        if (IsDisposed) return;
        if (!InvokeRequired) { a(); return; }
        try { BeginInvoke(a); } catch { }
    }

    string ultimo = "";
    // Atualiza a faixa do topo (e o registro quando o texto muda).
    void Estado(string texto, string sub, Color cor, bool pulsar, bool registrar = true)
    {
        if (registrar && texto + sub != ultimo) Log(texto + (sub == "" ? "" : " — " + sub));
        ultimo = texto + sub;
        Na(() => faixa.Mostrar(texto, sub, cor, pulsar));
    }

    // Lista e ícones dos campeões vêm do Data Dragon (público, sem chave). Ícones ficam guardados na pasta "icones".
    void CarregarCampeoes()
    {
        try
        {
            using (var web = new WebClient { Encoding = Encoding.UTF8 })
            {
                var versao = J.S(J.A(J.Ler(web.DownloadString("https://ddragon.leagueoflegends.com/api/versions.json"))).FirstOrDefault());
                var dados = J.D(J.G(J.D(J.Ler(web.DownloadString("https://ddragon.leagueoflegends.com/cdn/" + versao + "/data/pt_BR/champion.json"))), "data"));
                var lista = dados.Values.Select(J.D).Select(c => new Cartao { Id = J.S(J.G(c, "id")), Key = J.I(J.G(c, "key")), Nome = J.S(J.G(c, "name")), Tags = J.A(J.G(c, "tags")).Select(J.S).ToArray() })
                    .OrderBy(c => c.Nome, StringComparer.Create(new System.Globalization.CultureInfo("pt-BR"), true)).ToList();
                Invoke((Action)(() => MontarGrade(lista)));
                new Thread(() => CarregarRunas(versao)) { IsBackground = true }.Start();
                var pastaIcones = Path.Combine(Pasta, "icones", versao);
                Directory.CreateDirectory(pastaIcones);
                Parallel.ForEach(lista, new ParallelOptions { MaxDegreeOfParallelism = 6 }, c =>
                {
                    try
                    {
                        var arq = Path.Combine(pastaIcones, c.Id + ".png");
                        byte[] bytes = File.Exists(arq) ? File.ReadAllBytes(arq) : null;
                        if (bytes == null) { using (var w = new WebClient()) bytes = w.DownloadData("https://ddragon.leagueoflegends.com/cdn/" + versao + "/img/champion/" + c.Id + ".png"); File.WriteAllBytes(arq, bytes); }
                        Image img;
                        using (var ms = new MemoryStream(bytes)) using (var tmp = Image.FromStream(ms)) img = new Bitmap(tmp, 80, 80);
                        Na(() => { icones[c.Id] = img; c.Icone = img; if (vagas.Any(v => v.Id == c.Id)) Desenhar(); });
                    }
                    catch { }
                });
            }
        }
        catch (Exception e) { Estado("Sem lista de campeões", "Não consegui baixar (" + e.Message + "). Verifique a internet e abra de novo.", Cores.Laranja, false); }
    }

    void MontarGrade(List<Cartao> lista)
    {
        // config antigo pode ter nome escrito à mão; converte para o id do Data Dragon
        Func<string, string> achar = n => { var c = lista.FirstOrDefault(x => J.Norm(x.Id) == J.Norm(n) || J.Norm(x.Nome) == J.Norm(n)); return c != null ? c.Id : null; };
        lock (trava)
        {
            foreach (var k in picks.Keys.ToList()) picks[k] = picks[k].Select(achar).Where(x => x != null).ToList();
            foreach (var k in bans.Keys.ToList()) bans[k] = bans[k].Select(achar).Where(x => x != null).ToList();
            foreach (var k in runasCamp.Keys.ToList()) { var n = achar(k); var pg = runasCamp[k]; runasCamp.Remove(k); if (n != null) runasCamp[n] = pg; }
        }
        var dd = new Dictionary<int, string>(); foreach (var c in lista) if (c.Key > 0) dd[c.Key] = c.Id;
        ddPorKey = dd;
        grade.SuspendLayout();
        foreach (var c in lista) { var id = c.Id; c.Click += (o, e) => Clicar(id); grade.Controls.Add(c); }
        cartoes = lista;
        grade.ResumeLayout();
        Salvar(); Desenhar(); Filtrar();
    }

    List<int> Converter(List<string> nomes, HashSet<string> avisados)
    {
        var r = new List<int>();
        foreach (var n in nomes)
        {
            int id;
            if (idsLcu.TryGetValue(J.Norm(n), out id)) r.Add(id);
            else if (avisados.Add(n)) Log("Campeão desconhecido no config: " + n);
        }
        return r;
    }

    static string TextoFase(string f)
    {
        switch (f)
        {
            case "Lobby": return "No lobby";
            case "Matchmaking": return "Procurando partida...";
            case "InProgress": case "GameStart": return "Em jogo. Boa partida!";
            case "WaitingForStats": case "PreEndOfGame": case "EndOfGame": return "Fim de jogo";
            case "None": case null: return "No cliente";
            default: return f;
        }
    }

    void Laco()
    {
        Lcu con = null; int runasPara = 0; string posVista = null;
        var falhas = new HashSet<int>(); var avisados = new HashSet<string>();
        var marcados = new HashSet<string>(); var inicioBan = new Dictionary<int, DateTime>();
        while (true)
        {
            Thread.Sleep(1000);
            try
            {
                string tx;
                if (con == null)
                {
                    string lf;
                    // o cliente mantém o lockfile aberto: precisa abrir compartilhado
                    using (var fs = new FileStream(Path.Combine(pastaLol, "lockfile"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs)) lf = sr.ReadToEnd();
                    var partes = lf.Split(new[] { ':' });
                    con = new Lcu { Porta = partes[2], Auth = "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("riot:" + partes[3])) };
                    con.Req("GET", "/lol-game-data/assets/v1/champion-summary.json", null, out tx);
                    var ids = new Dictionary<string, int>(); var nomes = new Dictionary<int, string>();
                    foreach (var c in J.A(J.Ler(tx)).Select(J.D).Where(c => c != null))
                    {
                        int id = J.I(J.G(c, "id")); if (id <= 0) continue;
                        ids[J.Norm(J.S(J.G(c, "name")))] = id; ids[J.Norm(J.S(J.G(c, "alias")))] = id; nomes[id] = J.S(J.G(c, "name"));
                    }
                    idsLcu = ids; nomesLcu = nomes; avisados.Clear(); conAtual = con;
                    AtualizarFragmentos(con);
                    Estado("Conectado ao LoL", "Tudo pronto. Pode entrar na fila.", Cores.Verde, false);
                }
                bool aceita, comRunas; lock (trava) { aceita = aceitar; comRunas = runas; }

                con.Req("GET", "/lol-gameflow/v1/gameflow-phase", null, out tx);
                var fase = J.Ler(tx) as string;
                if (fase == "ReadyCheck")
                {
                    if (aceita) { con.Req("POST", "/lol-matchmaking/v1/ready-check/accept", null, out tx); Estado("Partida aceita!", "Indo para a seleção de campeões.", Cores.Verde, false); }
                    else Estado("Partida encontrada", "Aceitar automático está desligado.", Cores.Ouro, true);
                    continue;
                }
                if (fase != "ChampSelect")
                {
                    falhas.Clear(); marcados.Clear(); inicioBan.Clear(); runasPara = 0; posVista = null;
                    Estado(TextoFase(fase), fase == "Matchmaking" ? "Assim que achar, eu aceito." : "", fase == "Matchmaking" ? Cores.Azul : fase == "InProgress" ? Cores.Verde : Cores.Sutil, fase == "Matchmaking");
                    continue;
                }

                con.Req("GET", "/lol-champ-select/v1/session", null, out tx);
                var s = J.D(J.Ler(tx)); if (s == null) continue;
                int eu = J.I(J.G(s, "localPlayerCellId"));
                var acoes = J.A(J.G(s, "actions")).SelectMany(J.A).Select(J.D).Where(a => a != null).ToList();
                var me = J.A(J.G(s, "myTeam")).Select(J.D).FirstOrDefault(p => p != null && J.I(J.G(p, "cellId")) == eu);
                string pos = J.S(J.G(me, "assignedPosition")).ToLowerInvariant(); posAtual = pos;
                string ondeEstou = pos == "" ? "" : "Você caiu de " + NomePos(pos) + ".";
                // Ao ver a posição, mostra na janela a lista que vai ser usada.
                if (pos != posVista)
                {
                    posVista = pos;
                    bool conhecida = Posicoes.Any(x => x[0] == pos);
                    Na(() => MudarAlvo(conhecida ? pos : ""));
                    Estado(pos == "" ? "Seleção de campeões" : "Você caiu de " + NomePos(pos) + "!",
                        pos == "" ? "Modo sem rota: usando picks e bans de Qualquer." : "Usando picks e bans de " + NomePos(pos) + " (se acabarem, Qualquer).", Cores.Azul, false);
                }

                // Runas: depois que o seu campeão trava (por mim ou por você).
                int meu = J.I(J.G(me, "championId"));
                bool travou = acoes.Any(a => J.I(J.G(a, "actorCellId")) == eu && J.S(J.G(a, "type")) == "pick" && J.B(J.G(a, "completed")));
                if (comRunas && travou && meu > 0 && meu != runasPara)
                {
                    runasPara = meu;
                    Pagina pg = null; string dd; string origem = "suas runas";
                    if (ddPorKey.TryGetValue(meu, out dd)) lock (trava) runasCamp.TryGetValue(dd, out pg);
                    if (pg == null) { pg = Recomendada(con, meu, pos); origem = "recomendação do LoL"; }
                    if (pg == null) Log("Runas: sem página salva e sem recomendação do cliente para " + Nome(meu) + ".");
                    else CriarPagina(con, meu, pg, origem);
                }

                List<int> lp, lb;
                lock (trava) { lp = Converter(Regras.ListaPick(picks, pos), avisados); lb = Converter(Regras.ListaPick(bans, pos), avisados); }
                var minha = acoes.FirstOrDefault(a => J.I(J.G(a, "actorCellId")) == eu && J.B(J.G(a, "isInProgress")) && !J.B(J.G(a, "completed")));
                bool ehBan = minha != null && J.S(J.G(minha, "type")) == "ban";

                // Antes da sua vez: já marca o pick (o time vê) e troca se o marcado for banido ou pego.
                var meuPick = acoes.FirstOrDefault(a => J.I(J.G(a, "actorCellId")) == eu && J.S(J.G(a, "type")) == "pick" && !J.B(J.G(a, "completed")));
                if (minha == null)
                {
                    if (meuPick == null) { if (!travou) Estado("Seleção de campeões", "Esperando. " + ondeEstou, Cores.Ouro, true); continue; }
                    con.Req("GET", "/lol-champ-select/v1/pickable-champion-ids", null, out tx);
                    var h = Regras.Decidir(s, lp, lb, new HashSet<int>(J.A(J.Ler(tx)).Select(J.I)), meuPick);
                    int marcado = J.I(J.G(meuPick, "championId"));
                    if (h.Campeao != 0 && h.Campeao != marcado)
                    {
                        // tenta de novo a cada segundo (na fase de bans o cliente pode recusar), mas avisa só uma vez
                        int sm = con.Req("PATCH", "/lol-champ-select/v1/session/actions/" + J.I(J.G(meuPick, "id")), J.Escrever(new Dictionary<string, object> { { "championId", h.Campeao } }), out tx);
                        if (sm < 400) { marcado = h.Campeao; Log("Pick marcado: " + Nome(marcado)); }
                        else if (marcados.Add(J.I(J.G(meuPick, "id")) + ":" + h.Campeao)) Log("Ainda não deu para marcar " + Nome(h.Campeao) + " (erro " + sm + "). Tento de novo e travo na sua vez.");
                    }
                    Estado("Seleção de campeões", (marcado > 0 ? "Pick marcado: " + Nome(marcado) + ". Travo quando chegar a sua vez. " : "Esperando a sua vez. ") + ondeEstou, Cores.Ouro, true, false);
                    continue;
                }

                // Ban: espera 5 s para ver o que os aliados marcam antes de escolher.
                if (ehBan)
                {
                    int idBan = J.I(J.G(minha, "id")); DateTime t0;
                    if (!inicioBan.TryGetValue(idBan, out t0)) inicioBan[idBan] = t0 = DateTime.Now;
                    int falta = 5 - (int)(DateTime.Now - t0).TotalSeconds;
                    if (falta > 0) { Estado("Sua vez de banir", "Esperando " + falta + "s para ver o que o time quer jogar...", Cores.Vermelho, true, false); continue; }
                }
                con.Req("GET", ehBan ? "/lol-champ-select/v1/bannable-champion-ids" : "/lol-champ-select/v1/pickable-champion-ids", null, out tx);
                var disp = new HashSet<int>(J.A(J.Ler(tx)).Select(J.I));
                var d = Regras.Decidir(s, lp, lb.Where(x => !falhas.Contains(x)).ToList(), disp);
                if (d == null) continue;
                string verbo = d.Ban ? "banir" : "escolher";
                string pulos = string.Join(", ", d.Pulados.Select(p => Nome(p.Key) + " (" + p.Value + ")"));
                if (d.Campeao == 0) { Estado("Sua vez de " + verbo + ", mas ninguém da lista serve", pulos == "" ? "Lista vazia. " + ondeEstou : pulos, Cores.Laranja, true); continue; }
                if (pulos != "") Log("Pulei: " + pulos);
                // marca e depois confirma, em duas chamadas
                string url = "/lol-champ-select/v1/session/actions/" + J.I(J.G(d.Acao, "id"));
                int st = con.Req("PATCH", url, J.Escrever(new Dictionary<string, object> { { "championId", d.Campeao } }), out tx);
                if (st < 400) st = con.Req("POST", url + "/complete", null, out tx);
                if (st >= 400) { falhas.Add(d.Campeao); Log("Falhou ao " + verbo + " " + Nome(d.Campeao) + " (erro " + st + ") " + tx); }
                else if (d.Ban) Estado("Banido: " + Nome(d.Campeao), ondeEstou, Cores.Vermelho, false);
                else Estado("Travado: " + Nome(d.Campeao) + "!", ondeEstou, Cores.Azul, false);
            }
            catch (Exception e)
            {
                con = null; conAtual = null; // cliente fechou ou reiniciou: relê o lockfile (porta e senha mudam)
                Estado("Esperando o LoL abrir...", e is FileNotFoundException || e is DirectoryNotFoundException ? "Procurando o cliente em " + pastaLol : "", Cores.Sutil, true);
            }
        }
    }

    // Página que o próprio cliente recomenda para o campeão/rota (null se não tiver).
    Pagina Recomendada(Lcu con, int camp, string pos)
    {
        string tx;
        foreach (var p in new[] { pos == "" ? "NONE" : pos.ToUpperInvariant(), "NONE" }.Distinct())
        {
            if (con.Req("GET", "/lol-perks/v1/recommended-pages/champion/" + camp + "/position/" + p + "/map/11", null, out tx) >= 400) continue;
            var pag = J.A(J.Ler(tx)).Select(J.D).FirstOrDefault(x => x != null && J.A(J.G(x, "perks")).Length >= 9);
            if (pag == null) continue;
            return new Pagina { Primaria = J.I(J.G(pag, "primaryPerkStyleId")), Secundaria = J.I(J.G(pag, "secondaryPerkStyleId")),
                Perks = J.A(J.G(pag, "perks")).Select(x => x is Dictionary<string, object> ? J.I(J.G((Dictionary<string, object>)x, "id")) : J.I(x)).Take(9).ToList() };
        }
        return null;
    }

    // Cria a página "AutoPick <campeão>". Só apaga páginas cujo nome começa com AutoPick.
    void CriarPagina(Lcu con, int camp, Pagina pg, string origem)
    {
        string tx;
        con.Req("GET", "/lol-perks/v1/pages", null, out tx);
        foreach (var p in J.A(J.Ler(tx)).Select(J.D).Where(p => p != null && J.S(J.G(p, "name")).StartsWith("AutoPick") && !(J.G(p, "isDeletable") is bool && !(bool)J.G(p, "isDeletable"))))
            con.Req("DELETE", "/lol-perks/v1/pages/" + J.I(J.G(p, "id")), null, out tx);
        var corpo = J.Escrever(new Dictionary<string, object> {
            { "name", "AutoPick " + Nome(camp) }, { "primaryStyleId", pg.Primaria }, { "subStyleId", pg.Secundaria },
            { "selectedPerkIds", pg.Perks }, { "current", true } });
        int st = con.Req("POST", "/lol-perks/v1/pages", corpo, out tx);
        if (st >= 400) Log("Runas: não consegui criar a página (erro " + st + "). Se estiver no limite de páginas, renomeie uma para AutoPick que eu passo a usar ela. " + tx);
        else Estado("Runas prontas: " + Nome(camp), "Página \"AutoPick " + Nome(camp) + "\" com " + origem + ".", Cores.Roxo, false);
    }

    // Fragmentos atuais direto do cliente (mudam de vez em quando); nomes no idioma do cliente.
    void AtualizarFragmentos(Lcu con)
    {
        try
        {
            string tx;
            con.Req("GET", "/lol-perks/v1/styles", null, out tx);
            var est = J.A(J.Ler(tx)).Select(J.D).FirstOrDefault(x => x != null);
            var slots = J.A(J.G(est, "slots")).Select(J.D).Where(x => x != null && J.S(J.G(x, "type")) == "kStatMod").ToList();
            if (slots.Count != 3) return;
            con.Req("GET", "/lol-perks/v1/perks", null, out tx);
            var nomes = new Dictionary<int, string>();
            foreach (var p in J.A(J.Ler(tx)).Select(J.D).Where(x => x != null)) nomes[J.I(J.G(p, "id"))] = J.S(J.G(p, "name"));
            var novos = slots.Select(sl => J.A(J.G(sl, "perks")).Select(J.I).Select(id => new Fragmento { Id = id, Nome = nomes.ContainsKey(id) && nomes[id] != "" ? nomes[id] : id.ToString() }).ToList()).ToList();
            if (novos.All(l => l.Count > 0)) Na(() => DadosRunas.Fragmentos = novos);
        }
        catch { }
    }

    // Árvores e runas do Data Dragon (sempre do patch atual), ícones guardados em icones/runas.
    void CarregarRunas(string versao)
    {
        try
        {
            string json; using (var web = new WebClient { Encoding = Encoding.UTF8 }) json = web.DownloadString("https://ddragon.leagueoflegends.com/cdn/" + versao + "/data/pt_BR/runesReforged.json");
            var estilos = J.A(J.Ler(json)).Select(J.D).Where(e => e != null).Select(e => new Estilo
            {
                Id = J.I(J.G(e, "id")), Nome = J.S(J.G(e, "name")), Icone = J.S(J.G(e, "icon")),
                Linhas = J.A(J.G(e, "slots")).Select(J.D).Select(sl => J.A(J.G(sl, "runes")).Select(J.D).Where(r => r != null).Select(r => new Runa
                {
                    Id = J.I(J.G(r, "id")), Nome = J.S(J.G(r, "name")), Icone = J.S(J.G(r, "icon")),
                    Desc = Regex.Replace(J.S(J.G(r, "shortDesc")), "<[^>]+>", "")
                }).ToList()).ToList()
            }).ToList();
            Na(() => DadosRunas.Estilos = estilos);
            var pasta = Path.Combine(Pasta, "icones", "runas"); Directory.CreateDirectory(pasta);
            var caminhos = estilos.Select(e => e.Icone).Concat(estilos.SelectMany(e => e.Linhas).SelectMany(l => l).Select(r => r.Icone)).Where(c => c != "").Distinct().ToList();
            Parallel.ForEach(caminhos, new ParallelOptions { MaxDegreeOfParallelism = 6 }, c =>
            {
                try
                {
                    var arq = Path.Combine(pasta, c.Replace('/', '_'));
                    byte[] bytes = File.Exists(arq) ? File.ReadAllBytes(arq) : null;
                    if (bytes == null) { using (var w = new WebClient()) bytes = w.DownloadData("https://ddragon.leagueoflegends.com/cdn/img/" + c); File.WriteAllBytes(arq, bytes); }
                    Image img; using (var ms = new MemoryStream(bytes)) using (var tmp = Image.FromStream(ms)) img = new Bitmap(tmp, 64, 64);
                    Na(() => { DadosRunas.Imagens[c] = img; DadosRunas.Avisar(); });
                }
                catch { }
            });
        }
        catch (Exception e) { Log("Não consegui baixar as runas (" + e.Message + ")."); }
    }

    void AbrirRunas(string id)
    {
        var c = cartoes.FirstOrDefault(x => x.Id == id); if (c == null) return;
        if (DadosRunas.Estilos.Count == 0) { faixa.Mostrar("Runas ainda carregando", "Espere alguns segundos e tente de novo.", Cores.Laranja, false); return; }
        Pagina atual; lock (trava) runasCamp.TryGetValue(id, out atual);
        int key = c.Key; string pos = posAtual;
        string srv; lock (trava) srv = servidorRunas;
        using (var f = new RunasForm(c.Nome, atual, () => { var con = conAtual; return con == null ? null : Recomendada(con, key, pos); },
            key, pos, srv, url => { lock (trava) servidorRunas = url; Salvar(); }))
        {
            if (f.ShowDialog(this) != DialogResult.OK) return;
            lock (trava) { if (f.Resultado == null) runasCamp.Remove(id); else runasCamp[id] = f.Resultado; }
            Salvar(); Desenhar();
            faixa.Mostrar(f.Resultado == null ? "Runas de " + c.Nome + ": automáticas" : "Runas de " + c.Nome + " salvas!",
                f.Resultado == null ? "Vou usar a recomendação do LoL." : "Aplico quando você travar " + c.Nome + ".", Cores.Roxo, false);
        }
    }

    string Nome(int id) { string n; return nomesLcu.TryGetValue(id, out n) ? n : id.ToString(); }
}

static class Programa
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--teste")) return Teste();
        // Qualquer erro vira uma caixa de mensagem (e vai para autopick.log) em vez de fechar calado.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (o, e) => Erro(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (o, e) => Erro(e.ExceptionObject as Exception);
        try { Rodar(); } catch (Exception e) { Erro(e); }
        return 0;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void Rodar()
    {
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
        Application.EnableVisualStyles();
        Application.Run(new Janela());
    }

    static void Erro(Exception e)
    {
        var t = e == null ? "erro desconhecido" : e.ToString();
        try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "autopick.log"), DateTime.Now + "  ERRO: " + t + Environment.NewLine); } catch { }
        MessageBox.Show("O AutoPick deu erro. Mande este texto (Ctrl+C copia):\n\n" + t, "AutoPick LoL", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    static int Teste()
    {
        Func<string, Dictionary<string, object>> s = t => J.D(J.Ler(t.Replace('\'', '"')));
        var todos = new HashSet<int> { 1, 2, 3, 4, 5 };
        const string time = "'localPlayerCellId':0,'myTeam':[{'cellId':0},{'cellId':1,'championPickIntent':3}],'theirTeam':[]";
        var ban = s("{" + time + ",'bans':{},'actions':[[{'id':7,'actorCellId':0,'type':'ban','isInProgress':true,'completed':false}]]}");
        var pick = s("{" + time + ",'bans':{'theirTeamBans':[1]},'actions':[[{'actorCellId':1,'type':'pick','completed':true,'championId':2}],[{'id':9,'actorCellId':0,'type':'pick','isInProgress':true,'completed':false}]]}");
        var fora = s("{" + time + ",'actions':[]}");
        var listas = new Dictionary<string, List<string>> { { "", new List<string> { "Lux", "Ahri" } }, { "middle", new List<string> { "Ahri", "Zed" } } };
        var testes = new[] {
            Tuple.Create("pula o que o aliado quer", Regras.Decidir(ban, new List<int>(), new List<int> { 3, 4 }, todos).Campeao == 4),
            Tuple.Create("pula banido e já escolhido", Regras.Decidir(pick, new List<int> { 1, 2, 3 }, new List<int>(), todos).Campeao == 3),
            Tuple.Create("explica os pulados", Regras.Decidir(pick, new List<int> { 1, 2, 3 }, new List<int>(), todos).Pulados.Count == 2),
            Tuple.Create("só campeões que você tem", Regras.Decidir(pick, new List<int> { 1, 2, 3 }, new List<int>(), new HashSet<int> { 1, 2 }).Campeao == 0),
            Tuple.Create("marca o pick antes da vez", Regras.Decidir(pick, new List<int> { 1, 2, 3 }, new List<int>(), todos,
                s("{'id':9,'actorCellId':0,'type':'pick','isInProgress':false,'completed':false}")).Campeao == 3),
            Tuple.Create("secundária: 2 de linhas diferentes", string.Join(",", DadosRunas.Secundaria(DadosRunas.Secundaria(DadosRunas.Secundaria(new List<KeyValuePair<int, int>>(), 1, 10), 2, 20), 3, 30).Select(x => x.Value)) == "20,30"),
            Tuple.Create("secundária: mesma linha troca", string.Join(",", DadosRunas.Secundaria(DadosRunas.Secundaria(DadosRunas.Secundaria(new List<KeyValuePair<int, int>>(), 1, 10), 2, 20), 1, 11).Select(x => x.Value)) == "20,11"),
            Tuple.Create("página salva e lida", Pagina.De(J.Ler(J.Escrever(new Pagina { Primaria = 8100, Secundaria = 8200, Perks = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9 } }.ParaJson()))) != null),
            Tuple.Create("página incompleta é ignorada", Pagina.De(J.Ler("{\"primaria\":8100,\"secundaria\":8200,\"perks\":[1,2]}")) == null),
            Tuple.Create("não age fora da vez", Regras.Decidir(fora, new List<int> { 3 }, new List<int>(), todos) == null),
            Tuple.Create("posição primeiro, depois reserva", string.Join(",", Regras.ListaPick(listas, "middle")) == "Ahri,Zed,Lux"),
            Tuple.Create("sem posição usa reserva", string.Join(",", Regras.ListaPick(listas, "")) == "Lux,Ahri"),
            Tuple.Create("posição vazia usa reserva", string.Join(",", Regras.ListaPick(listas, "top")) == "Lux,Ahri"),
        };
        foreach (var t in testes.Where(t => !t.Item2)) Console.WriteLine("FALHOU: " + t.Item1);
        Console.WriteLine(testes.All(t => t.Item2) ? "ok" : "erro");
        return testes.All(t => t.Item2) ? 0 : 1;
    }
}
