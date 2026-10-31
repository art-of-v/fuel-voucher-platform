/**
 * What the reader shows about a contract.
 *
 * The reader is where the user looks before they sign, so what it shows has to be true: the
 * facts (which brand, which company, which version of the terms, the term it runs for) AND
 * the text of the terms itself. The text lives once per provider and is versioned, so the
 * server sends the version's text with the contract - `text` is null when that version has no
 * text yet, and absent when the card is about something else (a signature, which carries no
 * text of its own).
 */
export type ContractCard = {
  title: string;
  facts: { label: string; value: string }[];
  /** The text the customer reads before signing. Null: the version has no text. */
  text?: string | null;
};

/** The name the network is shown under when a contract's brand does not resolve. */
export const NETWORK_NAME = 'FuelFlow Network';
