using System.Drawing;
using System.Windows.Forms;

namespace SecureSign.FirmadorLocal;

/// <summary>
/// Ventana de selección de certificado + PIN — la parte "visible" del
/// Firmador Local, equivalente a lo que muestran Firma Perú/Adobe/DocuSign
/// en sus firmadores de escritorio. El PIN se enmascara con
/// <see cref="TextBox.UseSystemPasswordChar"/> y solo se lee su valor
/// después de que el propio usuario pulsa "Firmar" — nunca se registra ni
/// se muestra en la consola de fondo.
/// </summary>
internal sealed class VentanaFirma : Form
{
    private readonly ListBox _listaCertificados = new();
    private readonly TextBox _campoPin = new();
    private readonly Button _botonFirmar = new();

    public int IndiceCertificadoElegido { get; private set; } = -1;
    public string Pin { get; private set; } = string.Empty;

    public VentanaFirma(string nombreDocumento, long tamanoBytes, string hashHex, IReadOnlyList<string> descripcionesCertificados)
    {
        Text = "SecureSign Perú — Firmador Local";
        ClientSize = new Size(520, 400);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        Font = new Font("Segoe UI", 9F);

        var titulo = new Label
        {
            Text = "Firmar documento",
            Font = new Font(Font.FontFamily, 13F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(20, 16),
        };

        var infoDocumento = new Label
        {
            Text = $"Documento: {nombreDocumento}  ({tamanoBytes:N0} bytes)\nSHA-256: {hashHex}",
            AutoSize = false,
            Location = new Point(20, 52),
            Size = new Size(480, 40),
            ForeColor = Color.DimGray,
        };

        var avisoPin = new Label
        {
            Text = "Tu PIN se usa solo en esta máquina para firmar — nunca se envía a SecureSign.",
            AutoSize = false,
            Location = new Point(20, 96),
            Size = new Size(480, 18),
            ForeColor = Color.DimGray,
            Font = new Font(Font.FontFamily, 8F, FontStyle.Italic),
        };

        var etiquetaCertificados = new Label { Text = "Certificado de firma:", AutoSize = true, Location = new Point(20, 122) };
        _listaCertificados.Location = new Point(20, 144);
        _listaCertificados.Size = new Size(480, 130);
        _listaCertificados.HorizontalScrollbar = true;
        foreach (var descripcion in descripcionesCertificados)
            _listaCertificados.Items.Add(descripcion);
        if (_listaCertificados.Items.Count > 0)
            _listaCertificados.SelectedIndex = 0;

        var etiquetaPin = new Label { Text = "PIN de la tarjeta/token:", AutoSize = true, Location = new Point(20, 286) };
        _campoPin.Location = new Point(20, 308);
        _campoPin.Size = new Size(220, 24);
        _campoPin.UseSystemPasswordChar = true;

        var botonCancelar = new Button
        {
            Text = "Cancelar",
            Location = new Point(340, 350),
            Size = new Size(80, 30),
            DialogResult = DialogResult.Cancel,
        };

        _botonFirmar.Text = "Firmar";
        _botonFirmar.Location = new Point(420, 350);
        _botonFirmar.Size = new Size(80, 30);
        _botonFirmar.DialogResult = DialogResult.OK;
        _botonFirmar.Click += (_, _) =>
        {
            IndiceCertificadoElegido = _listaCertificados.SelectedIndex;
            Pin = _campoPin.Text;
        };

        AcceptButton = _botonFirmar;
        CancelButton = botonCancelar;

        Controls.AddRange(
        [
            titulo, infoDocumento, avisoPin, etiquetaCertificados, _listaCertificados, etiquetaPin, _campoPin, _botonFirmar, botonCancelar,
        ]);
    }
}
