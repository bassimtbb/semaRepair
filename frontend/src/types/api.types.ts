/** Car option shown during identification or DTC selection */
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

/** Car from DTC search — includes the document reference */
export interface DtcCarOption extends CarOption {
  siglaDocumento: string
  titoloDocumento: string
}

/** One structured repair case extracted by the LLM */
export interface RepairCase {
  sigla: string
  titolo: string
  stelle: number        // 1, 2, or 3
  impianto: string
  dispositivo: string
  causa: string
  dtc: string[]
  procedura: string
  nota: string | null
}

/** Response when identifying the car */
export interface IdentificationResponse {
  phase: 'identification'
  message: string
  carMatches: CarOption[]
  confirmed: boolean
  confirmedCar: CarOption | null
}

/** Response when showing cars for a DTC code */
export interface DtcCarsResponse {
  phase: 'dtc_cars'
  dtcCode: string
  message: string
  cars: DtcCarOption[]
  selectedCar: CarOption | null
}

/** Response when showing repair cases */
export interface RepairResponse {
  phase: 'chat'
  found: boolean
  message: string
  cases: RepairCase[]
}

/** Document with associated cars — shown during symptom search */
export interface SymptomDocument {
  siglaDocumento: string
  titoloDocumento: string
  cars: CarOption[]
}

/** Response when symptom search finds matching documents */
export interface SymptomCarsResponse {
  phase: 'symptom_cars'
  message: string
  documents: SymptomDocument[]
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
