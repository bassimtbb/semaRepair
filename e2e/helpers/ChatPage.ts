import { type Page, type Locator, expect } from '@playwright/test'

/**
 * Page Object Model for the SemaRepair chat UI.
 *
 * Streaming detection strategy:
 *  1. Wait for a new assistant-bubble to appear (count increases)
 *  2. If a typing-indicator appears, wait for it to detach (stream done)
 *  3. Return the last bubble's text
 */
export class ChatPage {
  readonly page: Page

  readonly input:          Locator
  readonly sendBtn:        Locator
  readonly assistantBubbles: Locator
  readonly typingIndicator: Locator
  readonly carCards:       Locator
  readonly carSelectBtns:  Locator
  readonly repairCards:    Locator
  readonly findAnotherBtns: Locator
  readonly suggestionBtns: Locator

  constructor(page: Page) {
    this.page             = page
    this.input            = page.getByTestId('chat-input')
    this.sendBtn          = page.getByTestId('send-btn')
    this.assistantBubbles = page.getByTestId('assistant-bubble')
    this.typingIndicator  = page.getByTestId('typing-indicator')
    this.carCards         = page.getByTestId('car-card')
    this.carSelectBtns    = page.getByTestId('car-select-btn')
    this.repairCards      = page.getByTestId('repair-card')
    this.findAnotherBtns  = page.getByTestId('find-another-btn')
    this.suggestionBtns   = page.getByTestId('suggestion-btn')
  }

  async goto() {
    await this.page.goto('/')
  }

  /** Type text and press Enter (or click Send). */
  async send(text: string) {
    await this.input.click()
    await this.input.fill(text)
    await this.sendBtn.click()
  }

  /**
   * Wait for streaming to finish after sending a message.
   * Returns the last assistant bubble's full text.
   */
  async waitForReply(countBefore: number, streamTimeout = 90_000): Promise<string> {
    // 1. New bubble appeared
    await this.page.waitForFunction(
      (n: number) => document.querySelectorAll('[data-testid="assistant-bubble"]').length > n,
      countBefore,
      { timeout: 15_000 },
    )

    // 2. If a typing-indicator showed up, wait for it to disappear
    try {
      await this.typingIndicator.waitFor({ state: 'visible', timeout: 3_000 })
    } catch {
      // Fast response — no indicator ever showed
    }
    try {
      await this.typingIndicator.waitFor({ state: 'detached', timeout: streamTimeout })
    } catch {
      // Already detached
    }

    return this.assistantBubbles.last().innerText()
  }

  /** Send a message and wait for the reply, returning the bubble text. */
  async chat(text: string, streamTimeout = 90_000): Promise<string> {
    const countBefore = await this.assistantBubbles.count()
    await this.send(text)
    return this.waitForReply(countBefore, streamTimeout)
  }

  /** Wait for at least one car-card to appear and return all visible cards. */
  async waitForCarCards(timeout = 20_000): Promise<Locator> {
    await this.carCards.first().waitFor({ state: 'visible', timeout })
    return this.carCards
  }

  /** Wait for at least one repair-card to appear. */
  async waitForRepairCard(timeout = 30_000): Promise<Locator> {
    await this.repairCards.first().waitFor({ state: 'visible', timeout })
    return this.repairCards
  }

  /** Click the first car's "Seleziona" button and wait for the next reply. */
  async selectFirstCar(streamTimeout = 90_000): Promise<string> {
    const countBefore = await this.assistantBubbles.count()
    await this.carSelectBtns.first().click()
    return this.waitForReply(countBefore, streamTimeout)
  }

  /** Click the "Cerca un altro documento" button in the given repair-card (first by default). */
  async clickFindAnother(cardIndex = 0, streamTimeout = 90_000): Promise<string> {
    const countBefore = await this.assistantBubbles.count()
    await this.findAnotherBtns.nth(cardIndex).click()
    return this.waitForReply(countBefore, streamTimeout)
  }

  /** Return the sigla attribute of a repair-card. */
  async getRepairCardSigla(index = 0): Promise<string | null> {
    return this.repairCards.nth(index).getAttribute('data-sigla')
  }
}
