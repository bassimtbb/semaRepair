"""
Database seeder.

Loads parsed data into the four tables in order:
  1. gup_rows              — raw Excel rows (2000)
  2. repair_documents      — one per unique document (108)
  3. repair_document_cars  — document <-> car join (666)
  4. car_embeddings        — unique car configs (157)

Every phase is idempotent — checks row count before inserting.
No embeddings are generated here. That is the backend's responsibility.
"""

import re
from psycopg2.extras import execute_values


def _clean(raw: str) -> str:
    """Strips formatting tags and normalizes whitespace."""
    if not raw:
        return ""
    text = re.sub(r'\[/?[A-Z0-9/]+\]', ' ', raw)
    text = re.sub(r'<[^>]+>', ' ', text)
    text = text.replace('&nbsp;', ' ')
    return re.sub(r'\s+', ' ', text).strip()


def _grado(capitolo: str) -> int:
    """
    Extracts reliability grade from chapter name.
    *** = 3, ** = 2, * = 1 (default)
    """
    if not capitolo:
        return 1
    if '(***)' in capitolo or '***' in capitolo:
        return 3
    if '(**)' in capitolo or '**' in capitolo:
        return 2
    return 1


def seed_gup_rows(conn, rows: list[dict]) -> None:
    """
    Phase 1 — Load raw Excel rows into gup_rows.
    Truncates and reloads if the count does not match.
    """
    with conn.cursor() as cur:
        cur.execute("SELECT COUNT(*) FROM gup_rows;")
        if cur.fetchone()[0] >= len(rows):
            print(f"  gup_rows already has {len(rows)} rows. Skipping.")
            return

        cur.execute("TRUNCATE TABLE gup_rows RESTART IDENTITY CASCADE;")

        execute_values(cur, """
            INSERT INTO gup_rows (
                id_macchina, marca_macchina, modello_macchina,
                anno_inizio_macchina, anno_fine_macchina,
                alimentazione_macchina, motorizzazione_macchina,
                kw_macchina, cavalli_macchina, codice_motore_macchina,
                id_documento, sigla_documento, titolo_documento,
                parole_chiave, capitolo_documento, contenuto_documento
            ) VALUES %s
        """, [(
            r["id_macchina"], r["marca_macchina"], r["modello_macchina"],
            r["anno_inizio_macchina"], r["anno_fine_macchina"],
            r["alimentazione_macchina"], r["motorizzazione_macchina"],
            r["kw_macchina"], r["cavalli_macchina"], r["codice_motore_macchina"],
            r["id_documento"], r["sigla_documento"], r["titolo_documento"],
            r["parole_chiave"], r["capitolo_documento"], r["contenuto_documento"]
        ) for r in rows], page_size=500)

    conn.commit()
    print(f"  Loaded {len(rows)} rows into gup_rows.")


def seed_repair_documents(conn) -> None:
    """
    Phase 2 — Populate repair_documents from gup_rows.
    Groups rows by sigla_documento and extracts the three chapters.
    Builds embed_text (title + keywords + identification) and search_vector.
    """
    with conn.cursor() as cur:
        cur.execute("SELECT COUNT(*) FROM repair_documents;")
        if cur.fetchone()[0] > 0:
            print("  repair_documents already populated. Skipping.")
            return

        cur.execute("""
            SELECT
                sigla_documento,
                MAX(titolo_documento) AS titolo,
                MAX(parole_chiave)    AS parole,
                MAX(CASE WHEN capitolo_documento ILIKE 'Grado%'
                    THEN capitolo_documento END) AS grado_cap,
                MAX(CASE WHEN capitolo_documento ILIKE 'Identificazione%'
                    THEN contenuto_documento END) AS identificazione_raw,
                MAX(CASE WHEN capitolo_documento ILIKE 'Procedura%'
                    THEN contenuto_documento END) AS procedura_raw
            FROM gup_rows
            WHERE sigla_documento IS NOT NULL
            GROUP BY sigla_documento
            ORDER BY sigla_documento;
        """)

        records = []
        for sigla, titolo, parole, grado_cap, id_raw, proc_raw in cur.fetchall():
            grado           = _grado(grado_cap or "")
            identificazione = _clean(id_raw or "")
            procedura       = _clean(proc_raw or "")

            # embed_text: the text that will be embedded by the backend.
            # Combines title + keywords + identification chapter —
            # the most semantically rich content for matching repair queries.
            embed_text = " ".join(filter(None, [
                titolo or "",
                parole or "",
                identificazione,
            ])).strip()

            records.append((
                sigla, titolo, parole, grado,
                identificazione, procedura, embed_text,
            ))

        execute_values(cur, """
            INSERT INTO repair_documents (
                sigla_documento, titolo_documento, parole_chiave,
                grado_attendibilita, identificazione, procedura, embed_text
            ) VALUES %s
            ON CONFLICT (sigla_documento) DO NOTHING;
        """, records)

        # Build full-text search vector for DTC code lookup
        cur.execute("""
            UPDATE repair_documents
            SET search_vector = to_tsvector(
                'simple',
                COALESCE(titolo_documento, '') || ' ' ||
                COALESCE(parole_chiave, '')    || ' ' ||
                COALESCE(identificazione, '')
            )
            WHERE search_vector IS NULL;
        """)

    conn.commit()
    print(f"  Populated {len(records)} repair documents.")


def seed_repair_document_cars(conn) -> None:
    """
    Phase 3 — Populate repair_document_cars.
    One row per unique (document, car configuration) pair.
    """
    with conn.cursor() as cur:
        cur.execute("SELECT COUNT(*) FROM repair_document_cars;")
        if cur.fetchone()[0] > 0:
            print("  repair_document_cars already populated. Skipping.")
            return

        cur.execute("""
            INSERT INTO repair_document_cars (
                repair_document_id,
                id_macchina, marca_macchina, modello_macchina,
                motorizzazione_macchina, codice_motore_macchina,
                alimentazione_macchina, anno_inizio_macchina,
                anno_fine_macchina, kw_macchina, cavalli_macchina
            )
            SELECT DISTINCT
                rd.id,
                g.id_macchina, g.marca_macchina, g.modello_macchina,
                g.motorizzazione_macchina, g.codice_motore_macchina,
                g.alimentazione_macchina, g.anno_inizio_macchina,
                g.anno_fine_macchina, g.kw_macchina, g.cavalli_macchina
            FROM gup_rows g
            JOIN repair_documents rd ON rd.sigla_documento = g.sigla_documento
            ON CONFLICT (repair_document_id, id_macchina) DO NOTHING;
        """)

        cur.execute("SELECT COUNT(*) FROM repair_document_cars;")
        count = cur.fetchone()[0]

    conn.commit()
    print(f"  Populated {count} repair document car links.")


def seed_car_embeddings(conn) -> None:
    """
    Phase 4 — Populate car_embeddings with unique car configurations.
    Builds embed_text for each car but leaves embedding = NULL.
    The backend generates embeddings when it starts up.
    """
    with conn.cursor() as cur:
        cur.execute("SELECT COUNT(*) FROM car_embeddings;")
        if cur.fetchone()[0] > 0:
            print("  car_embeddings already populated. Skipping.")
            return

        cur.execute("""
            SELECT DISTINCT
                id_macchina, marca_macchina, modello_macchina,
                motorizzazione_macchina, codice_motore_macchina,
                alimentazione_macchina, anno_inizio_macchina,
                anno_fine_macchina, kw_macchina, cavalli_macchina
            FROM gup_rows
            ORDER BY id_macchina;
        """)

        records = []
        for (id_mac, marca, modello, motor, codice,
             alim, anno_i, anno_f, kw, cv) in cur.fetchall():

            # Natural language description of the car — used as embedding input
            parts = [marca, modello]
            if motor:  parts.append(motor)
            if alim:   parts.append(alim)
            if kw:     parts.append(f"{kw}kw")
            if cv:     parts.append(f"{cv}cv")
            if anno_i: parts.append(str(anno_i))
            if anno_f: parts.append(str(anno_f))
            parts.append(codice)

            records.append((
                id_mac, marca, modello, motor, codice,
                alim, anno_i, anno_f, kw, cv,
                " ".join(parts),
            ))

        execute_values(cur, """
            INSERT INTO car_embeddings (
                id_macchina, marca_macchina, modello_macchina,
                motorizzazione_macchina, codice_motore_macchina,
                alimentazione_macchina, anno_inizio, anno_fine,
                kw, cavalli, embed_text
            ) VALUES %s
            ON CONFLICT (id_macchina) DO NOTHING;
        """, records)

        cur.execute("SELECT COUNT(*) FROM car_embeddings;")
        count = cur.fetchone()[0]

    conn.commit()
    print(f"  Populated {count} car configurations (embeddings pending).")
