"""
report.py — Generates vehicles_report.pdf

For every vehicle in GUP_PER_IA.xlsx the report shows:
  1. A vehicle header with all car attributes
  2. A list of all associated repair documents (sigla, title, anomalia)

Usage:
    python report.py
"""

from collections import defaultdict

from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import mm
from reportlab.platypus import (
    HRFlowable, KeepTogether, Paragraph, SimpleDocTemplate, Spacer,
)

from parser import parse_excel, _clean

EXCEL_PATH = "GUP_PER_IA.xlsx"
OUTPUT_PATH = "vehicles_report.pdf"

PAGE_W, PAGE_H = A4
MARGIN   = 20 * mm
USABLE_W = PAGE_W - 2 * MARGIN

GRAY     = colors.HexColor("#555555")
RULE     = colors.HexColor("#bbbbbb")

# ── Anomalia extractor ─────────────────────────────────────────────────────────
_STOPS = [
    "Impianto:", "Dispositivo:", "Anomalia:", "Errori rilevati",
    "Causa:", "Intervento:", "Procedura:", "Nota:",
]

def _extract_anomalia(text: str) -> str:
    if not text:
        return ""
    lo = text.lower()
    start = lo.find("anomalia:")
    if start < 0:
        return ""
    rest = text[start + len("anomalia:"):].strip()
    end = len(rest)
    for stop in _STOPS:
        idx = rest.lower().find(stop.lower())
        if 0 < idx < end:
            end = idx
    return rest[:end].strip()


# ── Data builder ───────────────────────────────────────────────────────────────

def build_data(rows: list[dict]):
    """
    Returns:
        cars_order : list[str]       — id_macchina in first-seen order
        cars       : dict[str, dict] — id_macchina -> car attribute dict
        doc_info   : dict[str, dict] — sigla -> {titolo, anomalia}
        car_docs   : dict[str, list] — id_macchina -> ordered list of sigla
    """
    cars_order: list[str] = []
    cars: dict[str, dict] = {}
    doc_info: dict[str, dict] = {}
    car_doc_seen: dict[str, set] = defaultdict(set)
    car_docs: dict[str, list] = defaultdict(list)

    for row in rows:
        mid   = row["id_macchina"]
        sigla = (row.get("sigla_documento") or "").strip()

        if mid not in cars:
            cars_order.append(mid)
            cars[mid] = {k: row[k] for k in (
                "id_macchina", "marca_macchina", "modello_macchina",
                "motorizzazione_macchina", "codice_motore_macchina",
                "alimentazione_macchina", "anno_inizio_macchina",
                "anno_fine_macchina", "kw_macchina", "cavalli_macchina",
            )}

        if not sigla:
            continue

        if sigla not in doc_info:
            doc_info[sigla] = {
                "titolo":   (row.get("titolo_documento") or "").strip(),
                "anomalia": "",
            }

        cap = (row.get("capitolo_documento") or "").lower()
        if cap.startswith("identificazione") and not doc_info[sigla]["anomalia"]:
            raw = row.get("contenuto_documento") or ""
            doc_info[sigla]["anomalia"] = _extract_anomalia(_clean(raw))

        if sigla not in car_doc_seen[mid]:
            car_doc_seen[mid].add(sigla)
            car_docs[mid].append(sigla)

    return cars_order, cars, doc_info, car_docs


# ── PDF builder ────────────────────────────────────────────────────────────────

def build_pdf(cars_order, cars, doc_info, car_docs):
    doc = SimpleDocTemplate(
        OUTPUT_PATH,
        pagesize=A4,
        leftMargin=MARGIN,
        rightMargin=MARGIN,
        topMargin=18 * mm,
        bottomMargin=18 * mm,
    )

    def style(name, **kw):
        defaults = dict(fontName="Helvetica", fontSize=9, leading=13,
                        textColor=colors.black)
        defaults.update(kw)
        return ParagraphStyle(name, **defaults)

    s_title      = style("Title",    fontName="Helvetica-Bold", fontSize=16,
                          leading=20, spaceAfter=3)
    s_subtitle   = style("Subtitle", fontSize=9, leading=12,
                          textColor=GRAY, spaceAfter=6)
    s_car_name   = style("CarName",  fontName="Helvetica-Bold", fontSize=11,
                          leading=15, spaceAfter=1)
    s_car_detail = style("CarDetail", fontSize=8.5, leading=12, textColor=GRAY)
    s_doc_line   = style("DocLine",  fontSize=9, leading=13, leftIndent=6)
    s_anom       = style("Anom",     fontSize=8.5, leading=12,
                          textColor=GRAY, leftIndent=6, spaceAfter=4)
    s_no_docs    = style("NoDocs",   fontSize=8.5, leading=12,
                          textColor=GRAY, leftIndent=6)

    story = []

    total_docs = sum(len(v) for v in car_docs.values())
    story.append(Paragraph("Veicoli e Documenti di Riparazione", s_title))
    story.append(Paragraph(
        f"{len(cars_order)} veicoli  -  {total_docs} associazioni documento",
        s_subtitle,
    ))
    story.append(Spacer(1, 5 * mm))

    for mid in cars_order:
        car    = cars[mid]
        siglas = car_docs.get(mid, [])

        name = " ".join(filter(None, [
            car["marca_macchina"],
            car["modello_macchina"],
            car.get("motorizzazione_macchina") or "",
        ]))
        anni  = f"{car['anno_inizio_macchina'] or '?'} - {car['anno_fine_macchina'] or '?'}"
        alim  = car.get("alimentazione_macchina") or "-"
        kw_cv = (
            f"{car['kw_macchina'] or '-'} kW / {car['cavalli_macchina'] or '-'} CV"
            if car.get("kw_macchina") or car.get("cavalli_macchina") else "-"
        )
        detail = (
            f"ID: {car['id_macchina']}   "
            f"Motore: {car['codice_motore_macchina']}   "
            f"{alim}   {anni}   {kw_cv}"
        )

        # The car header and its first document stay together across page breaks
        anchor = [
            Paragraph(name, s_car_name),
            Paragraph(detail, s_car_detail),
            Spacer(1, 3 * mm),
        ]

        if not siglas:
            anchor.append(Paragraph("Nessun documento associato.", s_no_docs))
            docs_tail = []
        else:
            first = siglas[0]
            info  = doc_info.get(first, {})
            anchor.append(Paragraph(
                f"<b>{first}</b>  {info.get('titolo') or '-'}", s_doc_line,
            ))
            anchor.append(Paragraph(
                f"Anomalia: {info.get('anomalia') or '-'}", s_anom,
            ))
            docs_tail = siglas[1:]

        story.append(KeepTogether(anchor))

        for sigla in docs_tail:
            info = doc_info.get(sigla, {})
            story.append(Paragraph(
                f"<b>{sigla}</b>  {info.get('titolo') or '-'}", s_doc_line,
            ))
            story.append(Paragraph(
                f"Anomalia: {info.get('anomalia') or '-'}", s_anom,
            ))

        story.append(Spacer(1, 4 * mm))
        story.append(HRFlowable(
            width="100%", thickness=0.5, color=RULE, spaceAfter=4 * mm,
        ))

    doc.build(story)
    print(f"PDF written: {OUTPUT_PATH}")


# ── Entry point ────────────────────────────────────────────────────────────────

if __name__ == "__main__":
    print("Parsing Excel...")
    rows = parse_excel(EXCEL_PATH)

    print("Building data model...")
    cars_order, cars, doc_info, car_docs = build_data(rows)

    print(f"Rendering PDF for {len(cars_order)} vehicles...")
    build_pdf(cars_order, cars, doc_info, car_docs)
