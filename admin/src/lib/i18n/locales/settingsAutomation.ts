import type { Language } from "../index";

export const settingsAutomation: Record<Language, Record<string, string>> = {
  en: {
    "settings.feedbackBotTitle": "In-app feedback",
    "settings.feedbackBotWhat":
      "Shows the Feedback tab in the customer app, which opens a private Telegram chat with a support bot. Messages land in the staff group and a reply typed there goes back to the customer. The bot itself is created with @BotFather and supplied through environment variables — this switch only turns the tab on.",
    "settings.enableFeedbackBot": "Enable the feedback channel",
    "settings.enableFeedbackBotHint":
      "When enabled, the Feedback tab appears in the customer app. Take it out of service by turning this off — no redeploy needed.",
    "settings.feedbackBotDisabledNote":
      "The feedback channel is OFF. The Feedback tab is hidden in the customer app.",
    "settings.feedbackBotEnabledNote":
      "The feedback channel is ON. The Feedback tab is visible in the customer app.",
    "settings.feedbackBotUnconfiguredNote":
      "The feedback channel cannot work yet: the Telegram bot token, staff group, webhook secret or signing key is missing from configuration. Add them to the API environment and restart it — until then the tab stays hidden whatever this switch says.",
    "settings.feedbackBotWebhookTitle": "Webhook",
    "settings.feedbackBotWebhookWhat":
      "Telegram delivers messages to this API. Register it after a deploy or a domain change; the URL is taken from the address you are browsing the admin panel on.",
    "settings.feedbackBotWebhookRegister": "Register webhook",
    "settings.feedbackBotWebhookRegistering": "Registering…",
    "settings.feedbackBotWebhookRegistered": "Webhook registered.",
    "settings.feedbackBotWebhookUrl": "URL",
    "settings.feedbackBotWebhookUnset": "not registered",
    "settings.feedbackBotWebhookPending": "Updates waiting",
    "settings.feedbackBotWebhookError": "Last error",
    "settings.feedbackBotWebhookUnknown":
      "Status unavailable — Telegram could not be reached.",
    "settings.voucherRenewalTitle": "Voucher renewal",
    "settings.voucherTermSaleTitle": "Short-term fuel selling",
    "settings.voucherTermSaleWhat":
      "Let customers buy fuel for a shorter validity in exchange for a discount. The voucher expires at the term they chose, and a shorter term costs less.",
    "settings.voucherTermSaleEnabledNote":
      "Customers can pick a term at checkout.",
    "settings.voucherTermSaleDisabledNote":
      "Customers buy the full remaining term at the normal price.",
    "settings.enableVoucherTermSale": "Enable short-term selling",
    "settings.enableVoucherTermSaleHint":
      "Shows a term picker at checkout with the tiers priced below.",
    "settings.voucherTermSaleTiersTitle": "Terms and discounts",
    "settings.voucherTermSaleTiersHint":
      "The discount is taken off the litre price for each term, bigger for a shorter term. A discount that would push the price below cost is refused at checkout.",
    "settings.voucherTermSaleDiscountHeader": "UAH/L off",
    "settings.marginBlocked":
      "Sells below cost on {0} of {1} fuels - checkout refuses these lines. Thinnest: {2}",
    "settings.marginThin":
      "Leaves under 0.50 ₴/L on {0} of {1} fuels - {2} ends at {3} ₴/L",
    "settings.voucherTermSaleOffered": "Offered",
    "settings.voucherTermSaleNotOffered": "Not offered",
    "settings.voucherRenewalWhat":
      "Lets customers pay to renew a voucher whose validity is almost up (or has lapsed): a still-valid voucher is extended in place, an expired one is replaced from stock. You set a per-term price in UAH per litre; a shorter term should cost less per litre than a longer one.",
    "settings.enableVoucherRenewal": "Enable voucher renewal",
    "settings.enableVoucherRenewalHint":
      "When off, the renew/replace option never appears in the app, whatever the term prices.",
    "settings.voucherRenewalDisabledNote":
      "Voucher renewal is currently OFF. Customers cannot renew or replace vouchers.",
    "settings.voucherRenewalEnabledNote":
      "Voucher renewal is ON. Only terms that are enabled and priced above zero are offered to customers.",
    "settings.voucherRenewalThreshold": "Trigger window (days)",
    "settings.voucherRenewalThresholdHint":
      "The renew/replace option appears once a voucher has this many days of validity left (or has already expired).",
    "settings.voucherRenewalTiersTitle": "Term prices (UAH per litre)",
    "settings.voucherRenewalTiersHint":
      "A term is offered only when it is enabled and its price is above zero.",
    "settings.voucherRenewalRateHeader": "UAH / litre",
    "settings.voucherRenewalOffered": "offered",
    "settings.voucherRenewalNotOffered": "not offered",
    "settings.voucherRenewalUnitWeek": "wk",
    "settings.voucherRenewalUnitMonth": "mo",
  },
  uk: {
    "settings.feedbackBotTitle": "Зворотний зв'язок у застосунку",
    "settings.feedbackBotWhat":
      "Показує вкладку «Зворотний зв'язок» у застосунку клієнта, яка відкриває приватний чат у Telegram із ботом підтримки. Повідомлення потрапляють у групу команди, а відповідь, написана там, повертається клієнту. самого бота створюєте через @BotFather і передаєте через змінні середовища — цей перемикач лише вмикає вкладку.",
    "settings.enableFeedbackBot": "Увімкнути канал зворотного зв'язку",
    "settings.enableFeedbackBotHint":
      "Коли увімкнено, вкладка «Зворотний зв'язок» з'являється в застосунку клієнта. Щоб вивести канал з експлуатації, вимкніть перемикач — redeploy не потрібен.",
    "settings.feedbackBotDisabledNote":
      "Канал зворотного зв'язку ВИМКНЕНО. Вкладка «Зворотний зв'язок» прихована в застосунку клієнта.",
    "settings.feedbackBotEnabledNote":
      "Канал зворотного зв'язку УВІМКНЕНО. Вкладка «Зворотний зв'язок» видима в застосунку клієнта.",
    "settings.feedbackBotUnconfiguredNote":
      "Канал зворотного зв'язку ще не може працювати: у конфігурації бракує токена бота Telegram, групи команди, секрету webhook або ключа підпису. Додайте їх у середовище API та перезапустіть його — до того вкладка лишатиметься прихованою, байдуже до стану цього перемикача.",
    "settings.feedbackBotWebhookTitle": "Webhook",
    "settings.feedbackBotWebhookWhat":
      "Telegram надсилає повідомлення на цей API. Зареєструйте його після деплою або зміни домену; URL береться з адреси, за якою ви відкрили адмінпанель.",
    "settings.feedbackBotWebhookRegister": "Зареєструвати webhook",
    "settings.feedbackBotWebhookRegistering": "Реєстрація…",
    "settings.feedbackBotWebhookRegistered": "Webhook зареєстровано.",
    "settings.feedbackBotWebhookUrl": "URL",
    "settings.feedbackBotWebhookUnset": "не зареєстровано",
    "settings.feedbackBotWebhookPending": "Очікують оновлень",
    "settings.feedbackBotWebhookError": "Остання помилка",
    "settings.feedbackBotWebhookUnknown":
      "Статус недоступний — Telegram не вдалося досягти.",
    "settings.voucherRenewalTitle": "Продовження талонів",
    "settings.voucherTermSaleTitle": "Продаж пального на короткий термін",
    "settings.voucherTermSaleWhat":
      "Дозволяє клієнтам купити пально на коротший термін зі знижкою. Талон спливає на обраний термін, і коротший термін коштує дешевше.",
    "settings.voucherTermSaleEnabledNote":
      "Клієнти можуть обрати термін під час оплати.",
    "settings.voucherTermSaleDisabledNote":
      "Клієнти купують повний термін за звичайною ціною.",
    "settings.enableVoucherTermSale": "Увімкнути продаж на короткий термін",
    "settings.enableVoucherTermSaleHint":
      "Показує пікер терміну під час оплати з цінами нижче.",
    "settings.voucherTermSaleTiersTitle": "Терміни та знижки",
    "settings.voucherTermSaleTiersHint":
      "Знижка віднімається від ціни за літр для кожного терміну, більша для коротшого терміну. Знижка, що опустила б ціну нижче собівартості, відхиляється під час оплати.",
    "settings.voucherTermSaleDiscountHeader": "грн/л",
    "settings.marginBlocked":
      "Продається нижче собівартості на {0} з {1} пальнів — оплата їх відхилить. Найтонше: {2}",
    "settings.marginThin":
      "Лишає менше 0,50 грн/л на {0} з {1} пальнів — {2} виходить на {3} грн/л",
    "settings.voucherTermSaleOffered": "Пропонується",
    "settings.voucherTermSaleNotOffered": "Не пропонується",
    "settings.voucherRenewalWhat":
      "Дає клієнтам змогу платно продовжити талон, термін якого добігає кінця (або вже минув): ще дійсний талон продовжується на місці, протермінований — замінюється зі складу. Ви задаєте ціну для кожного строку у грн за літр; коротший строк має коштувати дешевше за літр, ніж довший.",
    "settings.enableVoucherRenewal": "Увімкнути продовження талонів",
    "settings.enableVoucherRenewalHint":
      "Поки вимкнено, кнопка продовження/заміни не зʼявляється в застосунку незалежно від цін.",
    "settings.voucherRenewalDisabledNote":
      "Продовження талонів ВИМКНЕНО. Клієнти не можуть продовжувати чи замінювати талони.",
    "settings.voucherRenewalEnabledNote":
      "Продовження талонів УВІМКНЕНО. Клієнтам пропонуються лише строки, які увімкнені та мають ціну більшу за нуль.",
    "settings.voucherRenewalThreshold": "Вікно спрацювання (днів)",
    "settings.voucherRenewalThresholdHint":
      "Кнопка продовження/заміни зʼявляється, коли талону лишилося стільки днів дійсності (або він уже протермінований).",
    "settings.voucherRenewalTiersTitle": "Ціни за строк (грн за літр)",
    "settings.voucherRenewalTiersHint":
      "Строк пропонується, лише коли він увімкнений і його ціна більша за нуль.",
    "settings.voucherRenewalRateHeader": "грн / літр",
    "settings.voucherRenewalOffered": "пропонується",
    "settings.voucherRenewalNotOffered": "не пропонується",
    "settings.voucherRenewalUnitWeek": "тиж",
    "settings.voucherRenewalUnitMonth": "міс",
  },
  de: {
    "settings.feedbackBotTitle": "Feedback in der App",
    "settings.feedbackBotWhat":
      "Zeigt in der Kunden-App den Tab „Feedback“, der einen privaten Telegram-Chat mit einem Support-Bot öffnet. Nachrichten landen in der Team-Gruppe, und eine dort getippte Antwort geht zurück an die Kundin oder den Kunden. Den Bot selbst legst du über @BotFather an und lieferst ihn über Umgebungsvariablen — dieser Schalter blendet nur den Tab ein.",
    "settings.enableFeedbackBot": "Feedback-Kanal aktivieren",
    "settings.enableFeedbackBotHint":
      "Wenn aktiv, erscheint der Tab „Feedback“ in der Kunden-App. Zum Außerbetriebnehmen einfach diesen Schalter aus — kein Redeploy nötig.",
    "settings.feedbackBotDisabledNote":
      "Der Feedback-Kanal ist AUS. Der Tab „Feedback“ ist in der Kunden-App ausgeblendet.",
    "settings.feedbackBotEnabledNote":
      "Der Feedback-Kanal ist AN. Der Tab „Feedback“ ist in der Kunden-App sichtbar.",
    "settings.feedbackBotUnconfiguredNote":
      "Der Feedback-Kanal kann noch nicht funktionieren: Es fehlt mindestens einer von Bot-Token, Team-Gruppe, Webhook-Secret oder Signaturschlüssel. Trage sie in der API-Umgebung ein und starte sie neu — bis dahin bleibt der Tab ausgeblendet, was dieser Schalter auch zeigt.",
    "settings.feedbackBotWebhookTitle": "Webhook",
    "settings.feedbackBotWebhookWhat":
      "Telegram stellt Nachrichten an diese API zu. Registriere ihn nach einem Deployment oder Domainwechsel; die URL wird aus der Adresse übernommen, unter der du das Admin-Panel geöffnet hast.",
    "settings.feedbackBotWebhookRegister": "Webhook registrieren",
    "settings.feedbackBotWebhookRegistering": "Wird registriert…",
    "settings.feedbackBotWebhookRegistered": "Webhook registriert.",
    "settings.feedbackBotWebhookUrl": "URL",
    "settings.feedbackBotWebhookUnset": "nicht registriert",
    "settings.feedbackBotWebhookPending": "Wartende Updates",
    "settings.feedbackBotWebhookError": "Letzter Fehler",
    "settings.feedbackBotWebhookUnknown":
      "Status nicht verfügbar — Telegram war nicht erreichbar.",
    "settings.voucherRenewalTitle": "Gutschein-Verlängerung",
    "settings.voucherTermSaleTitle": "Kurzzeitiger Kraftstoffverkauf",
    "settings.voucherTermSaleWhat":
      "Kundinnen und Kunden kaufen Kraftstoff gegen Rabatt für eine kürzere Gültigkeit. Der Gutschein läuft zum gewählten Termin ab, und eine kürzere Laufzeit kostet weniger.",
    "settings.voucherTermSaleEnabledNote":
      "Kundinnen und Kunden können an der Kasse einen Termin wählen.",
    "settings.voucherTermSaleDisabledNote":
      "Kundinnen und Kunden kaufen die volle Restlaufzeit zum normalen Preis.",
    "settings.enableVoucherTermSale": "Kurzzeitverkauf aktivieren",
    "settings.enableVoucherTermSaleHint":
      "Zeigt an der Kasse eine Terminauswahl mit den unten preislichen Stufen.",
    "settings.voucherTermSaleTiersTitle": "Laufzeiten und Rabatte",
    "settings.voucherTermSaleTiersHint":
      "Der Rabatt wird vom Literpreis je Laufzeit abgezogen, bei kürzerer Laufzeit größer. Ein Rabatt, der den Preis unter Kosten drücken würde, wird an der Kasse abgelehnt.",
    "settings.voucherTermSaleDiscountHeader": "UAH/L ab",
    "settings.marginBlocked":
      "Wird bei {0} von {1} Kraftstoffen unter Kosten verkauft - die Zahlung lehnt diese Zeilen ab. Dünnste: {2}",
    "settings.marginThin":
      "Lässt weniger als 0,50 ₴/L bei {0} von {1} Kraftstoffen - {2} endet bei {3} ₴/L",
    "settings.voucherTermSaleOffered": "Angeboten",
    "settings.voucherTermSaleNotOffered": "Nicht angeboten",
    "settings.voucherRenewalWhat":
      "Ermöglicht Kundinnen und Kunden, einen fast abgelaufenen (oder bereits verfallenen) Gutschein kostenpflichtig zu verlängern: ein noch gültiger Gutschein wird direkt verlängert, ein verfallener aus dem Bestand ersetzt. Sie legen je Laufzeit einen Preis in UAH pro Liter fest; eine kürzere Laufzeit sollte pro Liter weniger kosten als eine längere.",
    "settings.enableVoucherRenewal": "Gutschein-Verlängerung aktivieren",
    "settings.enableVoucherRenewalHint":
      "Solange aus, erscheint die Verlängern/Ersetzen-Option nie in der App, unabhängig von den Preisen.",
    "settings.voucherRenewalDisabledNote":
      "Die Gutschein-Verlängerung ist AUS. Kundinnen und Kunden können Gutscheine nicht verlängern oder ersetzen.",
    "settings.voucherRenewalEnabledNote":
      "Die Gutschein-Verlängerung ist AN. Es werden nur Laufzeiten angeboten, die aktiviert und über null bepreist sind.",
    "settings.voucherRenewalThreshold": "Auslösefenster (Tage)",
    "settings.voucherRenewalThresholdHint":
      "Die Verlängern/Ersetzen-Option erscheint, sobald ein Gutschein noch so viele Tage gültig ist (oder bereits abgelaufen ist).",
    "settings.voucherRenewalTiersTitle": "Laufzeitpreise (UAH pro Liter)",
    "settings.voucherRenewalTiersHint":
      "Eine Laufzeit wird nur angeboten, wenn sie aktiviert und ihr Preis über null ist.",
    "settings.voucherRenewalRateHeader": "UAH / Liter",
    "settings.voucherRenewalOffered": "angeboten",
    "settings.voucherRenewalNotOffered": "nicht angeboten",
    "settings.voucherRenewalUnitWeek": "Wo.",
    "settings.voucherRenewalUnitMonth": "Mon.",
  },
  es: {
    "settings.feedbackBotTitle": "Comentarios en la app",
    "settings.feedbackBotWhat":
      "Muestra en la app del cliente la pestaña «Comentarios», que abre un chat privado de Telegram con un bot de soporte. Los mensajes llegan al grupo del equipo y una respuesta escrita allí vuelve al cliente. El bot se crea con @BotFather y se configura por variables de entorno; este interruptor solo muestra la pestaña.",
    "settings.enableFeedbackBot": "Activar el canal de comentarios",
    "settings.enableFeedbackBotHint":
      "Cuando está activo, la pestaña «Comentarios» aparece en la app del cliente. Para sacarlo de servicio, desactiva este interruptor: no hace falta redesplegar.",
    "settings.feedbackBotDisabledNote":
      "El canal de comentarios está DESACTIVADO. La pestaña «Comentarios» está oculta en la app del cliente.",
    "settings.feedbackBotEnabledNote":
      "El canal de comentarios está ACTIVADO. La pestaña «Comentarios» está visible en la app del cliente.",
    "settings.feedbackBotUnconfiguredNote":
      "El canal de comentarios aún no puede funcionar: falta el token del bot, el grupo del equipo, el secreto del webhook o la clave de firma en la configuración. Añádelos al entorno de la API y reiníciala; hasta entonces la pestaña seguirá oculta diga lo que diga este interruptor.",
    "settings.feedbackBotWebhookTitle": "Webhook",
    "settings.feedbackBotWebhookWhat":
      "Telegram entrega los mensajes a esta API. Regístralo tras un despliegue o un cambio de dominio; la URL se toma de la dirección con la que abriste el panel de administración.",
    "settings.feedbackBotWebhookRegister": "Registrar webhook",
    "settings.feedbackBotWebhookRegistering": "Registrando…",
    "settings.feedbackBotWebhookRegistered": "Webhook registrado.",
    "settings.feedbackBotWebhookUrl": "URL",
    "settings.feedbackBotWebhookUnset": "sin registrar",
    "settings.feedbackBotWebhookPending": "Actualizaciones pendientes",
    "settings.feedbackBotWebhookError": "Último error",
    "settings.feedbackBotWebhookUnknown":
      "Estado no disponible: no se pudo contactar con Telegram.",
    "settings.voucherRenewalTitle": "Renovación de vales",
    "settings.voucherTermSaleTitle": "Venta de combustible a corto plazo",
    "settings.voucherTermSaleWhat":
      "Permite comprar combustible con descuento a cambio de una validez más corta. El vale caduca en el plazo elegido, y un plazo más corto cuesta menos.",
    "settings.voucherTermSaleEnabledNote":
      "Los clientes pueden elegir un plazo al pagar.",
    "settings.voucherTermSaleDisabledNote":
      "Los clientes compran el plazo restante completo al precio normal.",
    "settings.enableVoucherTermSale": "Activar la venta a corto plazo",
    "settings.enableVoucherTermSaleHint":
      "Muestra un selector de plazo al pagar con los niveles de precio de abajo.",
    "settings.voucherTermSaleTiersTitle": "Plazos y descuentos",
    "settings.voucherTermSaleTiersHint":
      "El descuento se resta del precio por litro de cada plazo, mayor para un plazo más corto. Un descuento que dejaría el precio bajo coste se rechaza al pagar.",
    "settings.voucherTermSaleDiscountHeader": "UAH/L menos",
    "settings.marginBlocked":
      "Se vende bajo coste en {0} de {1} combustibles - el pago rechaza esas líneas. El más fino: {2}",
    "settings.marginThin":
      "Deja menos de 0,50 ₴/L en {0} de {1} combustibles - {2} queda en {3} ₴/L",
    "settings.voucherTermSaleOffered": "Ofrecido",
    "settings.voucherTermSaleNotOffered": "No ofrecido",
    "settings.voucherRenewalWhat":
      "Permite a los clientes pagar para renovar un vale cuya validez está por terminar (o ya venció): un vale aún válido se extiende in situ y uno vencido se reemplaza del stock. Fijas un precio por plazo en UAH por litro; un plazo más corto debería costar menos por litro que uno más largo.",
    "settings.enableVoucherRenewal": "Activar renovación de vales",
    "settings.enableVoucherRenewalHint":
      "Mientras está desactivada, la opción de renovar/reemplazar nunca aparece en la app, sea cual sea el precio.",
    "settings.voucherRenewalDisabledNote":
      "La renovación de vales está DESACTIVADA. Los clientes no pueden renovar ni reemplazar vales.",
    "settings.voucherRenewalEnabledNote":
      "La renovación de vales está ACTIVADA. Solo se ofrecen los plazos que están activados y con precio superior a cero.",
    "settings.voucherRenewalThreshold": "Ventana de activación (días)",
    "settings.voucherRenewalThresholdHint":
      "La opción de renovar/reemplazar aparece cuando a un vale le quedan estos días de validez (o ya venció).",
    "settings.voucherRenewalTiersTitle": "Precios por plazo (UAH por litro)",
    "settings.voucherRenewalTiersHint":
      "Un plazo se ofrece solo cuando está activado y su precio es superior a cero.",
    "settings.voucherRenewalRateHeader": "UAH / litro",
    "settings.voucherRenewalOffered": "ofrecido",
    "settings.voucherRenewalNotOffered": "no ofrecido",
    "settings.voucherRenewalUnitWeek": "sem",
    "settings.voucherRenewalUnitMonth": "mes",
  },
};
