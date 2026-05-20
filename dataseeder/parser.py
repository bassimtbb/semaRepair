"""
Excel parser for GUP_PER_IA.xlsx.

The Excel has a tricky format:
  - All 16 field values per row are packed into ONE Excel column as CSV text.
  - Some titles contain commas so Excel splits them across columns 2 and 3.
  - Fix: read all 3 Excel columns and join them before CSV parsing.
  - The title (field 12) is reconstructed by joining fields[12:-3].
  - The last 3 fields are always: parole_chiave, capitolo_documento, contenuto_documento.
"""

import csv
import io
import re
from typing import Optional
import openpyxl


def _safe_int(value: str) -> Optional[int]:
    """Converts a string to int. Returns None if empty or not numeric."""
    v = value.strip()
    if not v:
        return None
    try:
        return int(float(v))
    except (ValueError, TypeError):
        return None


def _fix_mojibake(s: str) -> str:
    """
    Repairs partially-fixed UTF-8 mojibake in the Excel source data.

    The original Italian chars (a`, e`, e', i`, o`, u`, degree, ...) were stored as
    UTF-8 bytes but read as Latin-1, producing two-character sequences:
      'Ã ' (Ã + NBSP)  for 'à'  (0xC3 0xA0)
      'Ã¨'             for 'è'  (0xC3 0xA8)
      'Ã©'             for 'é'  (0xC3 0xA9)
      'Â°'             for '°'  (0xC2 0xB0)   etc.

    A prior bulk find-replace changed 'Ã' to 'à' and left the second
    character in place, so we now see 'à' + NBSP, 'à¨', 'à©', 'Â°', ...

    Fix: for each such pair, reconstruct the original UTF-8 bytes and
    decode them. The lead byte is recovered from the known replacement:
      'à' (U+00E0) represents original byte 0xC3
      'Â' (U+00C2) represents original byte 0xC2
    The second character's code point equals its original byte value
    (valid for the Latin-1 supplement range U+0080-U+00BF).
    """
    if not s:
        return s

    def repair(lead_byte: int, m: re.Match) -> str:
        second_byte = ord(m.group(1))
        try:
            return bytes([lead_byte, second_byte]).decode('utf-8')
        except (UnicodeDecodeError, ValueError):
            return m.group(0)

    # 'à' (U+00E0) followed by U+0080-U+00BF: was originally 0xC3 0xXX in UTF-8
    s = re.sub('à([-¿])', lambda m: repair(0xC3, m), s)
    # 'Â' (U+00C2) followed by U+0080-U+00BF: was originally 0xC2 0xXX in UTF-8
    s = re.sub('Â([-¿])', lambda m: repair(0xC2, m), s)
    return s


def _clean(raw: str) -> str:
    """
    Strips SemaRepair HTML-like formatting tags.
    Tags like [BR/] [LI] [B] [/B] are removed.
    Whitespace is normalized.
    """
    if not raw:
        return ""
    text = re.sub(r'\[/?[A-Z0-9/]+\]', ' ', raw)
    text = re.sub(r'<[^>]+>', ' ', text)
    text = text.replace('&nbsp;', ' ')
    return re.sub(r'\s+', ' ', text).strip()


def parse_excel(filepath: str) -> list[dict]:
    """
    Reads the Excel file and returns a list of row dicts.
    Skips rows with missing id_macchina or id_documento.
    """
    wb = openpyxl.load_workbook(filepath, read_only=True, data_only=True)
    ws = wb.active

    rows = []
    skipped = 0

    for excel_row in ws.iter_rows(min_row=2, values_only=True):
        # Join all non-None Excel columns to reconstruct the full CSV line
        parts = [str(c) for c in excel_row if c is not None]
        if not parts:
            continue

        full_line = ','.join(parts)

        for fields in csv.reader(io.StringIO(full_line)):
            n = len(fields)
            if n < 16:
                skipped += 1
                continue

            id_macchina  = fields[0].strip()
            id_documento = fields[10].strip()

            if not id_macchina or not id_documento:
                skipped += 1
                continue

            # Reconstruct title — may have been split across columns
            titolo = _fix_mojibake(','.join(fields[12:n - 3]).strip())

            rows.append({
                "id_macchina":             id_macchina,
                "marca_macchina":          fields[1].strip(),
                "modello_macchina":        fields[2].strip(),
                "anno_inizio_macchina":    _safe_int(fields[3]),
                "anno_fine_macchina":      _safe_int(fields[4]),
                "alimentazione_macchina":  fields[5].strip() or None,
                "motorizzazione_macchina": fields[6].strip() or None,
                "kw_macchina":             _safe_int(fields[7]),
                "cavalli_macchina":        _safe_int(fields[8]),
                "codice_motore_macchina":  fields[9].strip(),
                "id_documento":            id_documento,
                "sigla_documento":         fields[11].strip() or None,
                "titolo_documento":        titolo or None,
                "parole_chiave":           _fix_mojibake(fields[n - 3].strip()) or None,
                "capitolo_documento":      _fix_mojibake(fields[n - 2].strip()) or None,
                # Raw content — cleaning happens in seeder.py
                "contenuto_documento":     _fix_mojibake(fields[n - 1].strip()) or None,
            })

    wb.close()

    print(f"Parsed {len(rows)} rows ({skipped} skipped).")

    from collections import Counter
    for brand, count in sorted(Counter(r["marca_macchina"] for r in rows).items()):
        print(f"  {brand:<12} {count:>4} rows")

    return rows
