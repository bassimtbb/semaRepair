import type { RepairCase, AlternativeDocumentResponse } from '../types/api.types'

const API_BASE = import.meta.env.VITE_API_BASE ?? ''

export async function fetchDocument(sigla: string): Promise<RepairCase> {
  const resp = await fetch(`${API_BASE}/api/documents/${encodeURIComponent(sigla)}`)
  if (!resp.ok) throw new Error(`${resp.status}`)
  return resp.json() as Promise<RepairCase>
}

/** Fallback: finds the best document for a car by its idMacchina. */
export async function fetchDocumentByCar(idMacchina: string): Promise<RepairCase> {
  const resp = await fetch(`${API_BASE}/api/documents/by-car/${encodeURIComponent(idMacchina)}`)
  if (!resp.ok) throw new Error(`Errore nel caricamento del documento: ${resp.status}`)
  return resp.json() as Promise<RepairCase>
}

/** Finds an alternative document for the same symptom, excluding the already-shown sigla. */
export async function fetchAlternativeDocument(
  symptom: string,
  engineCode: string | undefined,
  excludeSigla: string,
): Promise<AlternativeDocumentResponse> {
  const params = new URLSearchParams({ symptom, excludeSigla })
  if (engineCode) params.set('engineCode', engineCode)
  const resp = await fetch(`${API_BASE}/api/documents/alternative?${params}`)
  if (!resp.ok) throw new Error(`${resp.status}`)
  return resp.json() as Promise<AlternativeDocumentResponse>
}
