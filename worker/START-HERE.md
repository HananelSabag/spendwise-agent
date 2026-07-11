# SpendWise Worker — Start here

## עברית

1. חלץ את כל קובץ ה־ZIP לתיקייה רגילה. אל תפעיל מתוך ה־ZIP.
2. פתח את `SpendWiseWorker.exe` שבשורש התיקייה.
3. ב־SpendWise פתח **סנכרון בנקים → Agent → המחשב שלי** והעתק את קוד החיבור בן 8 התווים.
4. הדבק את הקוד ב־Worker ולחץ **התחבר**.
5. הפעל **הפעלה אוטומטית עם Windows** והשאר את ה־Worker פעיל ברקע.

זה הכול. Node.js ו־Chrome כבר כלולים בחבילה. התיקייה `app` היא מנוע ההפעלה ואין צורך לפתוח או לשנות אותה.

Windows עשוי להציג SmartScreen כי הקובץ עדיין לא חתום דיגיטלית. במקרה כזה בחר **מידע נוסף → הפעל בכל זאת**.

פרטי הכניסה לבנק מפוענחים רק במחשב הזה. אם איבדת את התיקייה, בטל את חיבור ה־Agent באתר וחבר מחדש.

## English

1. Extract the entire ZIP to a normal folder. Do not run it from inside the ZIP.
2. Open `SpendWiseWorker.exe` from the folder root.
3. In SpendWise, open **Bank Sync → Agent → My own computer** and copy the 8-character pairing code.
4. Paste the code into the Worker and click **Connect**.
5. Enable **Launch automatically with Windows** and leave the Worker running in the background.

Node.js and Chrome are bundled. The `app` folder is the runtime engine; you do not need to open or edit it.

Windows might show SmartScreen because the executable is not code-signed yet. Choose **More info → Run anyway**.

Bank credentials are decrypted only on this computer. If the folder is lost, disconnect the Agent in SpendWise and pair it again.
