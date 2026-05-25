/** Car option shown during identification */
export interface CarOption {
  idMacchina: string
  marca: string
  modello: string
  motorizzazione: string
  codiceMotore: string
  alimentazione: string
  annoInizio: number | null
  annoFine: number | null
  kw: number | null
  cavalli: number | null
}

/** Car from a search result — always has an associated document reference */
export interface SearchResultCar extends CarOption {
  siglaDocumento: string
  titoloDocumento: string
}

/** Car from DTC search — alias of SearchResultCar for backward compatibility */
export type DtcCarOption = SearchResultCar

/** One structured repair case returned from the document endpoint */
export interface RepairCase {
  sigla: string
  titolo: string
  stelle: number
  impianto: string
  dispositivo: string
  anomalia: string
  causa: string
  dtc: string[]
  /** Intervento subfield from the Procedura chapter (CONTENUTO_DOCUMENTO) */
  intervento: string
  /** Procedura subfield — null when the source value is a placeholder (- -) */
  procedura: string | null
  nota: string | null
  /** Engine codes of all cars this document applies to — used to verify relevance */
  engineCodes: string[]
}

/** Response when identifying the car */
export interface IdentificationResponse {
  phase: 'identification'
  message: string
  carMatches: SearchResultCar[]
  confirmed: boolean
  confirmedCar: CarOption | null
}

/** Response when showing cars for a DTC code */
export interface DtcCarsResponse {
  phase: 'dtc_cars'
  dtcCode: string
  message: string
  cars: SearchResultCar[]
  selectedCar: CarOption | null
}

/** A document title returned as a suggestion when no alternative exists for the confirmed car */
export interface DocumentSuggestion {
  sigla: string
  titolo: string
}

/** Response when showing repair cases */
export interface RepairResponse {
  phase: 'chat'
  found: boolean
  message: string
  cases: RepairCase[]
  /** Populated when found=false and there are related documents from a broader search */
  relatedSuggestions?: DocumentSuggestion[]
}

/** Response from GET /api/documents/alternative */
export interface AlternativeDocumentResponse {
  found: boolean
  document?: RepairCase
  message?: string
  relatedSuggestions?: DocumentSuggestion[]
}

/** Response when symptom or vehicle-only search finds matching cars */
export interface SymptomCarsResponse {
  phase: 'symptom_cars'
  message: string
  cars: SearchResultCar[]
  selectedCar: CarOption | null
}

/** Response when bot asks for car details after a fault code */
export interface AskCarResponse {
  phase: 'ask_car'
  codeDetected: string
  message: string
  confirmed: boolean
  carMatches: CarOption[]
  confirmedCar: CarOption | null
}

/** Union of all possible API responses */
export type ApiResponse =
  | IdentificationResponse
  | DtcCarsResponse
  | SymptomCarsResponse
  | AskCarResponse
  | RepairResponse
