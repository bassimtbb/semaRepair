import type { RepairCase } from '../types/api.types'

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
