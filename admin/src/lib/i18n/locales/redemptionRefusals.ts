import type { Language } from "../index";

/**
 * The sentences a refused redemption action or statement import is shown in, in the panel's
 * language. The codes come from `RedemptionRefusalCodes`; a code this file has not learnt keeps
 * the server's English sentence.
 */
export const redemptionRefusals: Record<Language, Record<string, string>> = {
  en: {
    "redemption.error.statementEmpty": "The statement has no lines to import.",
    "redemption.error.lineIdRequired":
      "Every statement line needs an external id.",
    "redemption.error.lineIdTooLong":
      "A statement line id is longer than 100 characters.",
    "redemption.error.lineVoucherRequired":
      "A statement line has no voucher number.",
    "redemption.error.lineVoucherTooLong":
      "A statement line's voucher number is longer than 100 characters.",
    "redemption.error.lineLitresNotPositive":
      "A statement line must state positive litres.",
    "redemption.error.actionInvalid":
      "The decision must be Confirm, Reject or Reverse.",
    "redemption.error.notFound": "That voucher is no longer there.",
    "redemption.error.invalidState":
      "The voucher's state does not allow this decision any more.",
    "redemption.error.lineNotFound": "That statement line is no longer there.",
    "redemption.error.lineNotDismissable":
      "This statement line's status does not allow dismissing it - resolve the voucher instead.",
    "redemption.error.lineHasPendingClaim":
      "This line belongs to a pending claim - confirm or reject the claim first.",
    "redemption.error.lineChanged":
      "The statement line changed before the dismiss landed - reload the queue.",
  },
  uk: {
    "redemption.error.statementEmpty": "У виписці немає рядків для імпорту.",
    "redemption.error.lineIdRequired":
      "Кожен рядок виписки потребує зовнішнього id.",
    "redemption.error.lineIdTooLong":
      "Ідентифікатор рядка виписки довший за 100 символів.",
    "redemption.error.lineVoucherRequired":
      "У рядка виписки немає номера талона.",
    "redemption.error.lineVoucherTooLong":
      "Номер талона в рядку виписки довший за 100 символів.",
    "redemption.error.lineLitresNotPositive":
      "Рядок виписки має містити додатні літри.",
    "redemption.error.actionInvalid":
      "Рішення має бути Confirm, Reject або Reverse.",
    "redemption.error.notFound": "Цього талона вже немає.",
    "redemption.error.invalidState": "Стан талона вже не дозволяє це рішення.",
    "redemption.error.lineNotFound": "Цього рядка виписки вже немає.",
    "redemption.error.lineNotDismissable":
      "Стан цього рядка не дозволяє його закрити - вирішіть талон.",
    "redemption.error.lineHasPendingClaim":
      "Цей рядок належить до заявки, що очікує рішення - спершу підтвердіть або відхиліть її.",
    "redemption.error.lineChanged":
      "Рядок змінився, поки закриття не дійшло - оновіть чергу.",
  },
  de: {
    "redemption.error.statementEmpty":
      "Der Auszug enthält keine Zeilen zum Importieren.",
    "redemption.error.lineIdRequired":
      "Jede Auszugszeile braucht eine externe ID.",
    "redemption.error.lineIdTooLong":
      "Eine Auszugs-ID ist länger als 100 Zeichen.",
    "redemption.error.lineVoucherRequired":
      "Eine Auszugszeile hat keine Gutscheinnummer.",
    "redemption.error.lineVoucherTooLong":
      "Die Gutscheinnummer einer Auszugszeile ist länger als 100 Zeichen.",
    "redemption.error.lineLitresNotPositive":
      "Eine Auszugszeile muss positive Liter angeben.",
    "redemption.error.actionInvalid":
      "Die Entscheidung muss Confirm, Reject oder Reverse sein.",
    "redemption.error.notFound": "Dieser Gutschein existiert nicht mehr.",
    "redemption.error.invalidState":
      "Der Status des Gutscheins erlaubt diese Entscheidung nicht mehr.",
    "redemption.error.lineNotFound": "Diese Auszugszeile existiert nicht mehr.",
    "redemption.error.lineNotDismissable":
      "Der Status dieser Zeile erlaubt kein Verwerfen - lösen Sie stattdessen den Gutschein.",
    "redemption.error.lineHasPendingClaim":
      "Diese Zeile gehört zu einem offenen Anspruch - bestätigen oder lehnen Sie ihn zuerst ab.",
    "redemption.error.lineChanged":
      "Die Auszugszeile hat sich vor dem Verwerfen geändert - laden Sie die Warteschlange neu.",
  },
  es: {
    "redemption.error.statementEmpty":
      "El extracto no tiene líneas para importar.",
    "redemption.error.lineIdRequired":
      "Cada línea del extracto necesita un id externo.",
    "redemption.error.lineIdTooLong":
      "Un id de línea del extracto supera los 100 caracteres.",
    "redemption.error.lineVoucherRequired":
      "Una línea del extracto no tiene número de vale.",
    "redemption.error.lineVoucherTooLong":
      "El número de vale de una línea supera los 100 caracteres.",
    "redemption.error.lineLitresNotPositive":
      "Una línea del extracto debe indicar litros positivos.",
    "redemption.error.actionInvalid":
      "La decisión debe ser Confirm, Reject o Reverse.",
    "redemption.error.notFound": "Ese vale ya no existe.",
    "redemption.error.invalidState":
      "El estado del vale ya no permite esta decisión.",
    "redemption.error.lineNotFound": "Esa línea del extracto ya no existe.",
    "redemption.error.lineNotDismissable":
      "El estado de esta línea no permite descartarla: resuelva el vale.",
    "redemption.error.lineHasPendingClaim":
      "Esta línea pertenece a una reclamación pendiente: confírmela o rechácela primero.",
    "redemption.error.lineChanged":
      "La línea cambió antes de aplicarse el descarte: recargue la cola.",
  },
};
