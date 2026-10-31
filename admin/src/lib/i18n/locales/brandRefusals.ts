import type { Language } from "../index";

/**
 * The sentences a refused brand or fuel write is shown in, in the panel's language.
 *
 * The codes come from `BrandRefusalCodes`; a code this file has not learnt keeps the server's
 * English sentence. It is its own module rather than part of `brands.ts` so the catalogue's
 * wording and its refusals stay two things a translator can work on.
 */
export const brandRefusals: Record<Language, Record<string, string>> = {
  en: {
    "brands.error.slugRequired":
      "A brand needs a slug - it is the network's own code.",
    "brands.error.slugTaken": "A brand with this slug already exists.",
    "brands.error.notFound": "That brand is no longer there.",
    "fuel.error.notFound": "That fuel is no longer there.",
    "fuel.error.testDataRequired":
      "Say whether this fuel is rehearsal data or a real product.",
    "fuel.error.nameExists":
      "This brand already sells a fuel with this name - edit that fuel instead of adding a second row.",
    "fuel.error.gradeExists":
      "This brand already sells the same grade as «{0}» - edit that fuel instead.",
    "fuel.error.codeTaken":
      "Another fuel at this brand already uses this code.",
    "fuel.error.rehearsalName":
      "The name contains «{0}», which reads as rehearsal data - mark the fuel as QA or rename it.",
    "fuel.error.poolMismatch":
      "This fuel's sizes decide whether it is sold live or as QA - a price edit cannot move it.",
    "fuel.error.liveStockNeedsCost":
      "This fuel has live stock on the shelf, so it must keep a cost.",
    "fuel.error.liveStockNeedsMargin":
      "This fuel has live stock on the shelf, so it must keep a margin.",
    "fuel.error.archiveBlocked":
      "This fuel still has {0} active voucher(s) - it can be archived only once they are used or expired.",
    "fuel.error.nominalsInvalid": "These sizes are not positive litres: {0}.",
    "fuel.error.costNotPositive":
      "The cost must be greater than zero - a zero cost is not a cost.",
    "fuel.error.marginNegative":
      "The margin cannot be negative - the fuel would be sold below what we paid.",
    "fuel.error.belowCostNeedsCost":
      "Allowing below-cost sales needs a cost to go below.",
  },
  uk: {
    "brands.error.slugRequired":
      "Бренду потрібен slug - це власний код мережі.",
    "brands.error.slugTaken": "Бренд із таким slug уже існує.",
    "brands.error.notFound": "Цього бренду вже немає.",
    "fuel.error.notFound": "Цього пального вже немає.",
    "fuel.error.testDataRequired":
      "Вкажіть, це тестове пальне чи справжній продукт.",
    "fuel.error.nameExists":
      "У цього бренду вже є пальне з такою назвою - відредагуйте його, а не додавайте другий рядок.",
    "fuel.error.gradeExists":
      "У цього бренду вже є та сама марка, що й «{0}» - відредагуйте її.",
    "fuel.error.codeTaken": "Інше пальне цього бренду вже має цей код.",
    "fuel.error.rehearsalName":
      "Назва містить «{0}», що схоже на тестові дані - позначте пальне як QA або перейменуйте.",
    "fuel.error.poolMismatch":
      "Пул визначають розміри пального, а редагування ціни їх не змінює.",
    "fuel.error.liveStockNeedsCost":
      "Це пальне має залишок на полиці, тому мусить мати собівартість.",
    "fuel.error.liveStockNeedsMargin":
      "Це пальне має залишок на полиці, тому мусить мати маржу.",
    "fuel.error.archiveBlocked":
      "Для цього пального ще активні талони: {0}. Архівувати можна лише після використання або прострочення.",
    "fuel.error.nominalsInvalid": "Ці об'єми не є додатними літрами: {0}.",
    "fuel.error.costNotPositive":
      "Собівартість має бути більшою за нуль - нульова собівартість не є собівартістю.",
    "fuel.error.marginNegative":
      "Маржа не може бути від'ємною - пальне продавалося б дешевше, ніж ми заплатили.",
    "fuel.error.belowCostNeedsCost":
      "Дозвіл продавати нижче собівартості потребує собівартості, нижче якої продавати.",
  },
  de: {
    "brands.error.slugRequired":
      "Ein Brand braucht einen Slug - das ist der eigene Code des Netzes.",
    "brands.error.slugTaken": "Ein Brand mit diesem Slug existiert bereits.",
    "brands.error.notFound": "Dieser Brand existiert nicht mehr.",
    "fuel.error.notFound": "Dieser Kraftstoff existiert nicht mehr.",
    "fuel.error.testDataRequired":
      "Geben Sie an, ob dieser Kraftstoff Testdaten oder ein echtes Produkt ist.",
    "fuel.error.nameExists":
      "Dieser Brand führt bereits einen Kraftstoff mit diesem Namen - bearbeiten Sie ihn statt eine zweite Zeile anzulegen.",
    "fuel.error.gradeExists":
      "Dieser Brand führt diese Sorte bereits als «{0}» - bearbeiten Sie sie stattdessen.",
    "fuel.error.codeTaken":
      "Ein anderer Kraftstoff dieses Brands verwendet diesen Code bereits.",
    "fuel.error.rehearsalName":
      "Der Name enthält «{0}», was nach Testdaten klingt - markieren Sie den Kraftstoff als QA oder benennen Sie ihn um.",
    "fuel.error.poolMismatch":
      "Die Größen des Kraftstoffs bestimmen seinen Pool - eine Preisbearbeitung ändert das nicht.",
    "fuel.error.liveStockNeedsCost":
      "Dieser Kraftstoff hat Ware im Regal und muss daher einen Einkaufspreis behalten.",
    "fuel.error.liveStockNeedsMargin":
      "Dieser Kraftstoff hat Ware im Regal und muss daher eine Marge behalten.",
    "fuel.error.archiveBlocked":
      "Für diesen Kraftstoff sind noch {0} Gutschein(e) aktiv - archivierbar erst, wenn sie verbraucht oder abgelaufen sind.",
    "fuel.error.nominalsInvalid":
      "Diese Mengen sind keine positiven Liter: {0}.",
    "fuel.error.costNotPositive":
      "Der Einkaufspreis muss größer als null sein - null ist kein Einkaufspreis.",
    "fuel.error.marginNegative":
      "Die Marge darf nicht negativ sein - der Kraftstoff würde unter dem Einkaufspreis verkauft.",
    "fuel.error.belowCostNeedsCost":
      "Der Verkauf unter Einkaufspreis braucht einen Einkaufspreis, unter den er gehen kann.",
  },
  es: {
    "brands.error.slugRequired":
      "Una marca necesita un slug: es el código propio de la red.",
    "brands.error.slugTaken": "Ya existe una marca con este slug.",
    "brands.error.notFound": "Esa marca ya no existe.",
    "fuel.error.notFound": "Ese combustible ya no existe.",
    "fuel.error.testDataRequired":
      "Indique si este combustible es de pruebas o un producto real.",
    "fuel.error.nameExists":
      "Esta marca ya vende un combustible con este nombre: edítelo en lugar de añadir otra fila.",
    "fuel.error.gradeExists":
      "Esta marca ya vende el mismo grado que «{0}»: edítelo en su lugar.",
    "fuel.error.codeTaken":
      "Otro combustible de esta marca ya usa este código.",
    "fuel.error.rehearsalName":
      "El nombre contiene «{0}», que parece de pruebas: márquelo como QA o cámbiele el nombre.",
    "fuel.error.poolMismatch":
      "Los tamaños del combustible deciden su grupo: editar el precio no lo cambia.",
    "fuel.error.liveStockNeedsCost":
      "Este combustible tiene existencias en el surtidor, así que debe mantener un coste.",
    "fuel.error.liveStockNeedsMargin":
      "Este combustible tiene existencias en el surtidor, así que debe mantener un margen.",
    "fuel.error.archiveBlocked":
      "Este combustible aún tiene {0} vale(s) activos: solo puede archivarse cuando se usen o caduquen.",
    "fuel.error.nominalsInvalid": "Estos tamaños no son litros positivos: {0}.",
    "fuel.error.costNotPositive":
      "El coste debe ser mayor que cero: un coste cero no es un coste.",
    "fuel.error.marginNegative":
      "El margen no puede ser negativo: el combustible se vendería por debajo de lo que pagamos.",
    "fuel.error.belowCostNeedsCost":
      "Permitir vender bajo coste requiere un coste bajo el que vender.",
  },
};
