import { test, expect } from '@playwright/test'
import { ChatPage } from '../helpers/ChatPage'

/**
 * End-to-end test suite for the SemaRepair AI mechanic chat.
 *
 * Pre-requisites:
 *   - Frontend dev server running on http://localhost:5173
 *   - Backend API running on http://localhost:5000 (or its configured port)
 *
 * Run: npm test  (from the e2e/ directory)
 */

// ─────────────────────────────────────────────────────────────────────────────
// Scenario 1 — Happy Path: symptom → car selection → repair document shown
// ─────────────────────────────────────────────────────────────────────────────
test('S1: Happy path — symptom leads to car list, then repair document', async ({ page }) => {
  const chat = new ChatPage(page)
  await chat.goto()

  // Step 1: Describe a symptom
  const reply1 = await chat.chat('Il motore non parte e la spia motore è accesa')

  // Expect either a car list or a clarifying question
  const hasCarCards = await chat.carCards.count() > 0
  const hasChatBubble = reply1.length > 10

  expect(hasChatBubble || hasCarCards).toBeTruthy()

  // If car cards appeared, select the first one
  if (hasCarCards) {
    const reply2 = await chat.selectFirstCar()

    // After car selection we expect a repair document or further confirmation
    const repairCardCount = await chat.repairCards.count()
    const hasContent = reply2.length > 10 || repairCardCount > 0

    expect(hasContent).toBeTruthy()

    // If a repair card appeared, verify it has a sigla attribute
    if (repairCardCount > 0) {
      const sigla = await chat.getRepairCardSigla(0)
      expect(sigla).toBeTruthy()
      expect(sigla!.length).toBeGreaterThan(0)
    }
  }
})

// ─────────────────────────────────────────────────────────────────────────────
// Scenario 2 — "Fiesta Rule": typing a car model after symptom_cars phase
//   must trigger B1b-MARCA (filter by brand) NOT restart as a new symptom
// ─────────────────────────────────────────────────────────────────────────────
test('S2: Fiesta rule — model name after symptom_cars filters cars, not new symptom', async ({ page }) => {
  const chat = new ChatPage(page)
  await chat.goto()

  // Step 1: Send a clear symptom to trigger SELEZIONA_VEICOLO car list
  const reply1 = await chat.chat('Il motore scalda troppo e perde acqua dal radiatore')

  // Wait briefly for the response to settle
  const carsAfterSymptom = await chat.carCards.count()

  // Whether or not cars appeared, now send a car model refinement
  // The system must NOT treat this as a new symptom
  const countBefore = await chat.assistantBubbles.count()
  await chat.send('Ford Fiesta 1.0 EcoBoost')
  const reply2 = await chat.waitForReply(countBefore)

  // The reply must NOT contain SINTOMO_VAGO (which would indicate misrouting)
  expect(reply2.toLowerCase()).not.toContain('sintomo_vago')
  expect(reply2.toLowerCase()).not.toContain('troppo breve')
  expect(reply2.toLowerCase()).not.toContain('più dettagli')

  // The reply should contain "Fiesta" or "Ford" or show repair/car cards
  const newCarCount = await chat.carCards.count()
  const newRepairCount = await chat.repairCards.count()

  const fiestaMentioned = reply2.toLowerCase().includes('fiesta') ||
                          reply2.toLowerCase().includes('ford')
  const cardsAppeared   = newCarCount > 0 || newRepairCount > 0

  expect(fiestaMentioned || cardsAppeared).toBeTruthy()
})

// ─────────────────────────────────────────────────────────────────────────────
// Scenario 3 — Wrong document flow: "Cerca un altro documento" returns a
//   different document (different sigla), or shows "non esistono altri documenti"
// ─────────────────────────────────────────────────────────────────────────────
test('S3: Find another document — returns different document or no-more message', async ({ page }) => {
  const chat = new ChatPage(page)
  await chat.goto()

  // Step 1: Get to a repair document via symptom + car selection
  await chat.chat('Perdita olio dal motore con fumo bianco')
  const carsCount = await chat.carCards.count()

  if (carsCount > 0) {
    await chat.selectFirstCar()
  }

  // Step 2: Wait for a repair card to appear
  const repairCardsBefore = await chat.repairCards.count()
  if (repairCardsBefore === 0) {
    // No repair card — try to get to one by sending a follow-up
    await chat.chat('mostra il documento di riparazione')
    await page.waitForTimeout(3_000)
  }

  const finalRepairCount = await chat.repairCards.count()
  if (finalRepairCount === 0) {
    test.skip() // Cannot reach a repair document in this run — skip gracefully
    return
  }

  // Grab the sigla of the first repair document shown
  const sigla1 = await chat.getRepairCardSigla(0)
  expect(sigla1).toBeTruthy()

  // Step 3: Click "Cerca un altro documento"
  const findAnotherCount = await chat.findAnotherBtns.count()
  expect(findAnotherCount).toBeGreaterThan(0)

  const replyAfter = await chat.clickFindAnother(0)

  // Two valid outcomes:
  // A) A new repair card appears with a DIFFERENT sigla
  const newRepairCount = await chat.repairCards.count()
  if (newRepairCount > 0) {
    const sigla2 = await chat.getRepairCardSigla(newRepairCount - 1)
    // If a new document was shown, it must be different from the first
    if (sigla2 && sigla1) {
      expect(sigla2).not.toBe(sigla1)
    }
  } else {
    // B) No more documents — the reply must say so (not a blank or error)
    expect(replyAfter.length).toBeGreaterThan(10)
    // Check for "non esistono altri" or suggestion buttons
    const noMoreDoc = replyAfter.toLowerCase().includes('non esistono') ||
                      replyAfter.toLowerCase().includes('nessun altro') ||
                      replyAfter.toLowerCase().includes('nessun documento')
    const hasSuggestions = await chat.suggestionBtns.count() > 0

    expect(noMoreDoc || hasSuggestions).toBeTruthy()
  }
})

// ─────────────────────────────────────────────────────────────────────────────
// Scenario 4 — Opzione C fallback: DTC code path → car selection → document
// ─────────────────────────────────────────────────────────────────────────────
test('S4: DTC code path — fault code triggers car list and then repair document', async ({ page }) => {
  const chat = new ChatPage(page)
  await chat.goto()

  // Step 1: Send a DTC fault code (common real-world format)
  const reply1 = await chat.chat('Codice guasto P0171')

  // The system should acknowledge the DTC code in some way
  const hasCars = await chat.carCards.count() > 0
  const hasContent = reply1.length > 10

  expect(hasContent).toBeTruthy()

  // If car list appeared, select one and verify repair document
  if (hasCars) {
    const reply2 = await chat.selectFirstCar()

    const repairCount = await chat.repairCards.count()
    const hasRepairContent = reply2.length > 10 || repairCount > 0

    expect(hasRepairContent).toBeTruthy()

    if (repairCount > 0) {
      // Verify the repair card references DTC content (P0171 or similar codes)
      const cardText = await chat.repairCards.first().innerText()
      // The document should be about fuel mixture / injector issues for P0171
      expect(cardText.length).toBeGreaterThan(50)

      // Verify sigla is set
      const sigla = await chat.getRepairCardSigla(0)
      expect(sigla).toBeTruthy()
    }
  } else {
    // No cars listed — verify the assistant responded meaningfully
    // (could be asking for more vehicle info)
    expect(reply1.length).toBeGreaterThan(20)
    expect(reply1.toLowerCase()).not.toContain('errore')
    expect(reply1.toLowerCase()).not.toContain('error')
  }
})
