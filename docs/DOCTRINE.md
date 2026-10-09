# Doctrine: the study behind the agent's instructions

Since version 0.6 (Oct 9), the war room's agent speaks as a medieval commander and carries a short doctrine: 14 principles, each grounded in medieval military writing or a campaign. This page is the study behind them, with its sources. It stays in the repository: the agent never reads it (CLAUDE.md, "the agent's folder stays outside the repo").

## Why, and the rule

**Mert, Oct 9:** "I want a better system prompt for the agent. It should act like a super intelligent military commander from the middle ages: for that, I want you to study doctrines and military tactics from the middle ages to really nail down the persona we want the agent to embody as well as its general strategy. It shouldn't 'have to' defend castles it is going to lose if it is smarter for it to wait out, amass a huge force, and defeat a large enemy army AND take back what it has lost, plus maybe some more, in a counter-offensive. It is important that it stays agressive but doesn't lose sight of the bigger picture and the long game."

**The rule, also Mert's:** "I don't want to give it the strategy that I just gave you word for word, that would be polluting the test. It should arrive at that conclusion on its own."

So the instructions teach general principles with their history, and never this war's plan. What Mert said the orcs should do in this war is in [What was left out on purpose](#what-was-left-out-on-purpose).

**Where it changed the instructions** (`agent/warroom-system.template.md`, installed as `warroom-system.md`):
- **A new "Who you are":** the commander's persona, in eight lines, each tied to a writer or a campaign.
- **A new goal:** "Break the enemy's power to make war on you ... Keep your own ... Grow." The 0.4 goal's first line, "Keep every castle and town you hold", is gone: it pushed the agent to defend every place, which is the opposite of the doctrine (holdings are means; a field army lost for a doomed castle is the worse loss).
- **A new "Doctrine" section:** 14 principles, below.
- **The council of war:** what a council is for ([WARROOM.md](WARROOM.md#the-council-of-war)).
- **Two wording changes:**
  - lords without the agent's orders are led by the game's own AI, said plainly;
  - "change only what the poke needs" now applies to pokes, with the council setting the whole realm's course.
- **The examples:** neutral ids (`lord_A`, `castle_B`) instead of a relief of Western Moria, which named the very place of Mert's test.

## How the study was done

Three research passes on Oct 9, each reading sources online (reviews and summaries where a book was not freely readable, marked below):
1. **The written doctrine:**
   - Vegetius;
   - the Byzantine manuals (Maurice's *Strategikon*, Leo VI's *Taktika*, *On Skirmishing*, the *Praecepta militaria*, Ouranos, Kekaumenos);
   - Islamic writers (al-Harawi, Nizam al-Mulk, Ibn Khaldun, al-Ansari);
   - late medieval Western writers (Christine de Pizan, Jean de Bueil).
2. **Campaigns:**
   - the scholarly debate on seeking or avoiding battle (Smail, Gillingham, Rogers, Morillo, Bachrach);
   - cases: Ain Tuba'un 1183, Hattin 1187, Richard I 1191–92, William of Normandy, Edward III and the Black Prince, Charles V and du Guesclin, Basil II, Bouvines, Henry V.
3. **Sieges, raids, prisoners, supply, and the steppe and Mamluk masters:**
   - sieges and relief: Calais, Harfleur, Rouen, Lincoln, Orléans, Nicopolis;
   - raiding, ransom, and the life of armies;
   - the Mongols (May, Pow, Laszlovszky);
   - Saladin, Ain Jalut and Baybars (Amitai, Gibb, Baldwin, Fuess).

**Caveats the passes reported:**
- Some quotations reached us through scholars, not the texts themselves: al-Harawi through Torres; Christine de Pizan through Rech; Smail through Gillingham, Rogers and Roach. Rogers' *War Cruel and Sharp*, Allmand's book on Vegetius, Morgan, and Rogers' 1994 essay (beyond its abstract) were not read.
- Figures marked as from encyclopedias summarise Sumption and others, so treat them as approximate.
- The archive.org copies of Dennis' translations were uploaded by the public; cite the printed books.
- **No proof that commanders read Vegetius:** Titterton calls the maxims "banal commonplaces", and the story of Geoffrey Plantagenet reading Vegetius at a siege (dated 1150–51, not 1147, by Dutton) is praise, not evidence.

## The debate the doctrine has to settle: seek battle or avoid it?

- **Avoid it:**
  - Smail (1956): the Franks in Syria usually won "by other means which did not involve the penalties of defeat": an army kept whole near an invader stopped sieges and plunder.
  - Gillingham (1984, 1989): Richard I fought two or three battles in about 25 years of war; Henry II none. Normal war was ravaging, then sieges, because "a few minutes of confusion or panic and the patient work of months or years might be undone".
- **Seek it:** Rogers (1994, 2000, 2002) calls that view "fundamentally flawed".
  - Edward III and the Black Prince wrote that they meant "to make an end to the war by battle". If refusing battle profits the defender, forcing one must profit the attacker.
  - Refusing costs burnt land, lost rents and lost loyalty.
  - Battles did decide wars: Poitiers, Agincourt.
- **In between:**
  - Morillo (2002): much of Rogers' evidence is "battle threatening", offering battle while expecting a refusal; Edward sought battle only on ground that gave him every advantage. Wars over land between settled states favour caution; wars for one central throne favour battle.
  - D. S. Bachrach: the Ottonian kings ran about 100 operations against fortresses and fought about 20 battles, all but one when an enemy came to break a siege.
- **The writers disagree the same way:**
  - for refusing: the *Strategikon* ("never be enticed into a pitched battle") and Leo VI;
  - for striking when the odds favour you: Vegetius, the *Praecepta*, Kekaumenos and Jean de Bueil.

**The doctrine's answer** (principle 3) is the research's working rule: the side that time works against (far from home, its allies about to leave, weaker in the long run) must seek battle, on ground it chooses. The side that time favours can refuse it. Either way, fight only with an edge, or when forced, and strike at once when the edge is yours.

## The 14 principles, and what each rests on

| # | Principle (as the agent reads it, shortened) | What it rests on | What it means in this game |
|---|---|---|---|
| 1 | The army is the realm; castles are its anchors. You owe no place a hopeless defence. | Hattin 1187: garrisons emptied, the army destroyed in a day, the castles fallen within months; 1183, the same army kept whole, the land saved. Strategikon VIII.2.45: "The general is at fault if most of the army is destroyed in a single battle." Calais, Harfleur, Rouen fell without relief. | Lost men come back slowly (recruiting needs gold and villages; [LOGGING.md](LOGGING.md)); lords captured leave the war. A holding can be retaken. |
| 2 | A siege is a clock: relieve with a stronger force, or let it go (or strike the enemy's lands to pull the besiegers home). | Lincoln 1217 and Orléans 1429, relieved; Calais 1347, where the relief looked at the English works and left; *On Skirmishing* 20 (invade back to pull the besiegers home); Nicopolis 1396 (besiegers caught by a relief army). | The game's sally rule: the garrison and the lords inside join a relief only above 1.5 times the besiegers' strength ([TOOLS.md](TOOLS.md)). A weak relief dies alone. |
| 3 | Battle on your terms; the side time works against must seek it. | Vegetius III.26 ("Good generals never engage in a general engagement except when opportunity offers, or under great necessity"), III.9 (a battle is settled "in two or three hours"; when superior, "let him be not slow"); the debate above. | Strength is shown everywhere (reports, facts, muster); the agent can weigh it. |
| 4 | Squeeze an invader you cannot yet fight: follow it, pick off its parties. | Vegetius III.3 (if they gather they starve, if they disperse they are beaten in detail); *On Skirmishing* 22; Normandy 1054, Jerusalem 1183, France 1373 (Gaunt's raid followed, never fought, wrecked by hunger). | Hunt, screen and defend tools; armies in the game starve and lose cohesion. |
| 5 | Concentrate; march apart, meet at one place on one day. | Mongols 1241 (Legnica and Mohi two days apart; separate columns meeting at set places and times, May 2015); Hattin as the warning against stripping every garrison. | gather, and join for far-off lords (0.6); the muster table shows what could be where, and when. |
| 6 | Strike the divided enemy. | Mortemer 1054, Varaville 1057, Pontvallain 1370 (after the English captains quarrelled and split), Alnwick 1174, Bouvines 1214. | Enemy lords and armies in view and last seen; engage, hunt. |
| 7 | Raiders on their way home. | *On Skirmishing* 4 ("never let them return home unscathed"); Leo VI, *Taktika* 18.128; Leo Phokas, 950. | Rohan's small lords raid the orcs' western villages; hunt and screen. |
| 8 | Armies live on food and purpose; idle armies break up. | Vegetius III.3 ("hunger is more savage than the sword"), III.26 ("improved by work, enfeebled by inactivity"), III.1 (over-large armies beaten by their own size); a Third Crusade death list (14 of 98 recorded as drowned, captured or killed; Gillingham); forty-day service; Saladin's army going home from Tyre. | The game breaks up an army idle about two days, out of cohesion, or half starving; every order carries a food rule. |
| 9 | Raid with a purpose. | Edward III and the Black Prince (the 1355 raid's report of towns that paid "the wages of a thousand men-at-arms"); Ouranos (raid a fortress' land before besieging it); raids rarely conquer (Knolles 1370, Gaunt 1373). | Raid tool; looted villages stop giving recruits. |
| 10 | Exploit victory, keep the lords together, leave no base. | *Strategikon* VII.B.12 ("a near-miss is still a complete miss"); Vegetius III.26 (a pursuit with scattered forces throws the victory away); Saladin after Hattin (Acre in about a week, Jerusalem by 2 October) and Tyre left as the Third Crusade's base. | After a won battle, enemy lords are captured and its holdings weakly held. |
| 11 | Lords are the sinews of war. | Gillingham: "the surest way to win a battle was to kill or capture the opposing commander"; Poitiers 1356 (King John captured); Hastings; Alnwick. | Every captured lord is a party gone; the report lists the realm's own. |
| 12 | Hold what you can support; strike where the enemy is absent. | Baybars 1265–71 (razed coastal places, kept inland Safed, passed by Acre and Tripoli, sealed Antioch's port and passes first); Saladin razing Ascalon; Richard turning back from Jerusalem, given "the improbability that the city could be long held even if it were taken" (Painter); Ain Jalut after Hülegü left. | The frontier list shows enemy holdings and garrisons in sight. |
| 13 | After a defeat, rally fast; then think long. | Vegetius III.25 ("human minds are carried away by success"); Kekaumenos WJ 15 (gather even a quarter of your men and strike that day); Basil II; Charles V; Jean de Bueil ("who endures, wins"). | Recruiting is slow; the council resets the realm's course after a loss. |
| 14 | Initiative with judgment. | Kekaumenos WJ 10 ("let most of your actions be daring"); the taunts of cowardice after 1183 and the gamble of 1187 (Gillingham; Kedar); Philip VI mocked by his own nobles in 1339. | The agent is asked what it can do to the enemy, not only what the enemy can do. |

## The persona's eight lines, and their grounding

- **Counting before moving:**
  - *On Skirmishing* opens with watch posts and spies;
  - the Mongols decided a campaign in council on gathered intelligence (May 2015);
  - Kekaumenos WJ 9: "You can't find out about the enemy except by having many scouts."
- **Battle only with the edge, or when forced:** Vegetius III.26.
- **Strike without delay when superior:** Vegetius III.9; Kekaumenos WJ 10.
- **Strike the divided enemy:** Mortemer and Varaville (Gillingham, "William the Bastard at War").
- **Let the invader burn out:**
  - Charles V and du Guesclin, 1369–80: the king's ruling that Knolles' army "should not then be fought with";
  - Pontvallain;
  - Gaunt's raid of 1373 followed to Bordeaux;
  - by 1375 England held only Calais, Cherbourg, Brest, Bordeaux, Bayonne and a few forts.
- **March apart, strike together, armies before castles:** Legnica (9 April 1241) and Mohi (11 April); "The Mongols preferred to deal with all field armies before moving deep into enemy territory" (May 2015).
- **Strike where the enemy is absent:** Ain Jalut, 3 September 1260, after Hülegü left with most of the army (Amitai).
- **Exploit, and leave no base:** Saladin after Hattin, and Tyre (Gibb, Baldwin).

## What was left out on purpose

- **This war's plan, as Mert described it:** mass one great army, beat Aragorn's host, take the places that hold the most captured orc lords to free them, rebuild, then counter-attack with everyone.
  - The doctrine states the general principles Mert now explicitly wants available: no doomed castle needs a hopeless defence; concentrate; win with the edge; exploit; retake.
  - Whether, when and against whom to do any of it is the agent's to decide.
- **Freeing one's own captured lords by taking the place that holds them,** although the third research pass recommended it (Guy freed for Ascalon; Prince Leon's ransom paid in forts). It is part of the plan above. The agent sees where its captured lords are held, grouped by place; what it does with that is the test.
- **Diplomacy maxims** (sow discord among allies, accept peace on good terms, Kekaumenos on paying tribute): the agent has no diplomatic orders yet. They come with the coalition (plan, week 3).
- **Battlefield tactics** (the feigned retreat, formations, the charge at Arsuf): the game fights the battles.
- **Ransom and the killing of prisoners:** not in the agent's orders.

## What would show it working

These are signs to watch for in Mert's standard test: load the Summer 9, 1087 save, then march on Western Moria with more than 5,000 men. They are not instructions to the agent.
- Its council, after the first look, reads the balance of strength (its field armies against the host coming) and sets a course for the whole realm, not one place.
- It does not feed a relief to a besieged place lord by lord, below the strength that would make the garrison sally out.
- It masses before it strikes, or refuses battle and harasses, and it strikes when the enemy divides or wears out.
- After a win, it follows up at once.
- Its plan and lessons say why, in its own words.

## Sources

**Primary texts, in translation**
- Vegetius, *Epitome of Military Science*, tr. N. P. Milner (Liverpool UP, 1993), Book III: https://facultystaff.richmond.edu/~wstevens/history331texts/Vegetius.html ; Latin text: https://www.thelatinlibrary.com/vegetius3.html ; tr. John Clarke (1767), III.26: https://regimentalrogue.com/bl2/Vegetius-General-Maxims.html
- *Maurice's Strategikon*, tr. G. T. Dennis (1984), and Leo VI, *Taktika*, ed. and tr. G. T. Dennis (Dumbarton Oaks, 2010): https://archive.org/details/maurice-strategikon-dennis-1984
- "Skirmishing" (*De velitatione*), in G. T. Dennis, *Three Byzantine Military Treatises* (Dumbarton Oaks, 1985): https://archive.org/details/cfhb-25-three-byzantine-military-treatises-1985
- *Praecepta militaria* and Ouranos, *Taktika* 56–65, in E. McGeer, *The Byzantine Army in the 10th Century* (PhD thesis, Montreal, 1990; the basis of *Sowing the Dragon's Teeth*, 1995): https://umontreal.scholaris.ca/items/48cf066e-7bbf-403c-b9c9-339e3d69dce2
- Kekaumenos, *Consilia et Narrationes*, tr. Charlotte Roueché (SAWS, 2013): https://ancientwisdoms.ac.uk/library/kekaumenos-consilia-et-narrationes/
- Nizam al-Mulk, *The Book of Government*, tr. Hubert Darke (1960): https://archive.org/details/the-book-of-government-or-rules-for-kings
- Ibn Khaldun, *Muqaddimah*, tr. Franz Rosenthal (1958), ch. 3 sec. 35: https://www.muslimphilosophy.com/ik/Muqaddimah/Chapter3/Ch_3_35.htm
- al-Ansari, *A Muslim Manual of War*, ed. and tr. G. T. Scanlon (1961; 2012, front matter only): https://archive.org/details/a-000-t-tafrj-al-kurub-fi-tadbir-al-hurub
- Jean de Bueil, *Le Jouvencel*, ed. C. Favre and L. Lecestre (1887–89): https://archive.org/details/lejouvencel01buei , https://archive.org/details/lejouvencel02buei
- Roger of Wendover, *Flowers of History*, tr. J. A. Giles, vol. 2 (1849): https://archive.org/details/rogerofwendovers02roge
- *The Chronicle of Novgorod 1016–1471*, tr. R. Michell and N. Forbes (1914): https://archive.org/details/chronicleofnovgo00michrich
- Hattin chronicle extracts (*De Expugnatione*, Ernoul, Imad ad-Din, Baha ad-Din), Fordham sourcebook translations: https://sourcebooks.fordham.edu/source/1187ernoul.asp

**Scholarship**
- Gillingham, J., "Richard I and the Science of War in the Middle Ages" (1984): https://deremilitari.org/wp-content/uploads/2013/08/gillingham2.pdf ; "William the Bastard at War" (1989): https://deremilitari.org/wp-content/uploads/2014/04/gillingham3.pdf ; "Surrender in Medieval Europe" (2012): https://www.medievalists.net/2015/01/surrender-medieval-europe-indirect-approach/ ; "Coeur de Lion in Captivity" (2013): https://www.medievalists.net/2014/08/coeur-de-lion-captivity/
- Rogers, C. J., "The Vegetian 'Science of Warfare' in the Middle Ages", and Morillo, S., "Battle Seeking: The Contexts and Limits of Vegetian Strategy", *Journal of Medieval Military History* 1 (2002): https://www.jstor.org/stable/10.7722/j.ctt7zssh1 ; Rogers, "Edward III and the Dialectics of Strategy, 1327–1360" (1994), abstract: https://www.cambridge.org/core/journals/transactions-of-the-royal-historical-society/article/abs/edward-iii-and-the-dialectics-of-strategy-13271360-the-alexander-prize-essay/D553FA0B032DF43AF87E29D2E0386B58
- Bachrach, D. S., review of Rogers, *Essays on Medieval Military History*, H-France Review 10 (2010): https://www.h-france.net/vol10reviews/vol10no150Bachrach.pdf
- Titterton, J. W., *Trickery and Deception in Medieval Warfare* (PhD thesis, Leeds, 2019): https://etheses.whiterose.ac.uk/id/eprint/24194/1/Titterton%20Thesis%20Master%20Final.pdf
- Dutton, K. A., *Geoffrey, Count of Anjou* (PhD thesis, Glasgow, 2011): https://theses.gla.ac.uk/3052/1/2011DuttonPhD.pdf
- Wheeler, E., review of C. Allmand, *The De Re Militari of Vegetius* (2011): https://reviews.history.ac.uk/review/1293/
- Kedar, B. Z., "The Battle of Hattin Revisited" (1992): https://deremilitari.org/2025/01/the-battle-of-hattin-revisited/ ; Roach, D., "Contemporary perceptions of the Battle of Hattin" (Exeter): https://deremilitari.org/wp-content/uploads/2016/10/roach.pdf
- Setton, K. M. (ed.), *A History of the Crusades*, vols. I and II (1969; chapters by Gibb, Baldwin, Painter, Strayer, Runciman, Der Nersessian): https://archive.org/details/setton-wisconsin-history-of-the-crusades-vol-1 , https://archive.org/details/setton-wisconsin-history-of-the-crusades-vol-2
- Holmes, C., "Basil II", *De Imperatoribus Romanis*: https://roman-emperors.sites.luc.edu/basilii.htm ; May, T., review of Stephenson, *The Legend of Basil the Bulgar-Slayer*: https://www.deremilitari.org/REVIEWS/Stephenson_basil.htm
- May, T., "Mongol Warfare in the Pre-Dissolution Period", *Golden Horde Review* (2015): http://goldhorde.ru/wp-content/uploads/2015/12/%D0%97%D0%9E-2-2015-6-20.pdf ; Claussen, S., review of May, *The Mongol Art of War* (2021): https://deremilitari.org/2021/04/timothy-may-the-mongol-art-of-war-samuel-claussen/ ; Albright, N., review of Sverdrup, *The Mongol Conquests* (2018): https://deremilitari.org/2018/02/carl-fredrik-sverdrup-the-mongol-conquests-albright/
- Pow, S., "Deep Ditches and Well-Built Walls" (MA thesis, Calgary, 2012): https://ucalgary.scholaris.ca/server/api/core/bitstreams/f409356a-4e0d-4fa2-afa7-7add6193f94b/content ; Laszlovszky, J., et al., "Contextualizing the Mongol Invasion of Hungary in 1241–42" (2018): https://hunghist.org/83-articles/515-2018-2-laszlovszky ; Pinke, Z., et al., "Climate of doubt" (2017): https://pmc.ncbi.nlm.nih.gov/articles/PMC5629221/
- Amitai-Preiss, R., *Mongols and Mamluks* (1995), chapter summaries: https://www.cambridge.org/core/books/abs/mongols-and-mamluks/the-battle-of-ayn-jlut/28AF2C512086301FC77D6DC25EAAF02D ; Fuess, A., "Rotting Ships and Razed Harbors", *Mamlūk Studies Review* 5 (2001): https://knowledge.uchicago.edu/record/1019
- McMahon, L., "De Velitatione Bellica and Byzantine Guerrilla Warfare" (2016): https://ams.ceu.edu/2016/McMahon.pdf
- Torres, O., "Los consejos de al-Harawi sobre estratagemas de guerra" (2017): https://dialnet.unirioja.es/descarga/articulo/6429172.pdf ; Rech, W., "Christine de Pizan on the laws of war" (2024): https://academic.oup.com/lril/article/12/1/47/7682371
- Ambühl, R., *Prisoners of War in the Hundred Years War* (summary and review): https://www.medievalists.net/2010/04/prisoners-of-war-in-the-hundred-years-war-the-golden-age-of-private-ransoms/ , https://scholarworks.iu.edu/journals/index.php/tmr/article/download/18561/24674
- Georgopoulou, M., review of Kennedy, *Crusader Castles* (1997): https://scholarworks.iu.edu/journals/index.php/tmr/article/view/14466/20584
- Latham, A., and Brown, R. L., "'War, Cruel and Sharp': England's Grand Strategy during the Hundred Years' War" (2020): https://www.medievalists.net/2020/04/england-grand-strategy-hundred-years-war/

**Encyclopedia articles** (dates and figures only; most summarise Sumption and Curry): the battles of al-Fule, Hattin, Arsuf, Jaffa (1192), Poitiers, Pontvallain, Kleidion, Bouvines, Nicopolis, the Kalka and Evesham; the sieges of Tyre (1187), Acre (1189–91), Calais (1346–47), Harfleur, Rouen (1418–19), Orléans and Tournai (1340); the Black Prince's raid of 1355 and John of Gaunt's of 1373; the Treaty of Brétigny; knight service; on en.wikipedia.org, read in October 2026.
