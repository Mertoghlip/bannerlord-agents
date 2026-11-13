# Doctrine: the study behind the agent's instructions

Since version 0.6 (Oct 9), the war room's agent speaks as a medieval commander and carries a short doctrine: 29 principles (14 since Oct 9, the 15th since Oct 16, the 16th and 17th since Oct 17, the 18th since Oct 25, the 19th since Nov 1; the 7th corrected on Oct 29; all 19 checked against the game's rules and 20 to 29 added on Nov 13), each grounded in medieval military writing or a campaign. This page is the study behind them, with its sources. It stays in the repository: the agent never reads it, since the agent's folder stays outside the repo.

## Why, and the rule

**Oct 9:** I want a better system prompt for the agent. It should act like a super intelligent military commander from the middle ages: for that, the study covers doctrines and military tactics from the middle ages, to really nail down the persona we want the agent to embody as well as its general strategy. It shouldn't "have to" defend castles it is going to lose if it is smarter for it to wait out, amass a huge force, and defeat a large enemy army AND take back what it has lost, plus maybe some more, in a counter-offensive. It is important that it stays aggressive but doesn't lose sight of the bigger picture and the long game.

**The rule, also mine:** I don't want to give it the strategy above word for word: that would be polluting the test. It should arrive at that conclusion on its own.

So the instructions teach general principles with their history, and never this war's plan. What I said the orcs should do in this war is in [What was left out on purpose](#what-was-left-out-on-purpose).

**Where it changed the instructions** (`agent/warroom-system.template.md`, installed as `warroom-system.md`):
- **A new "Who you are":** the commander's persona, in eight lines, each tied to a writer or a campaign.
- **A new goal:** "Break the enemy's power to make war on you ... Keep your own ... Grow." The 0.4 goal's first line, "Keep every castle and town you hold", is gone: it pushed the agent to defend every place, which is the opposite of the doctrine (holdings are means; a field army lost for a doomed castle is the worse loss).
- **A new "Doctrine" section:** 14 principles, below (a 15th added on Oct 16).
- **The council of war:** what a council is for ([WARROOM.md](WARROOM.md#the-council-of-war)).
- **Two wording changes:**
  - lords without the agent's orders are led by the game's own AI, said plainly;
  - "change only what the poke needs" now applies to pokes, with the council setting the whole realm's course.
- **The examples:** neutral ids (`lord_A`, `castle_B`) instead of a relief of Western Moria, which named the very place of my test.

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

## The 19 principles, and what each rests on

As written before Nov 13. The sweep of that day checked every one against the game's rules and added ten more ([below](#the-game-fit-sweep-and-principles-20-to-29-nov-13)).

| # | Principle (as the agent reads it, shortened) | What it rests on | What it means in this game |
|---|---|---|---|
| 1 | The army is the realm; castles are its anchors. You owe no place a hopeless defence. | Hattin 1187: garrisons emptied, the army destroyed in a day, the castles fallen within months; 1183, the same army kept whole, the land saved. Strategikon VIII.2.45: "The general is at fault if most of the army is destroyed in a single battle." Calais, Harfleur, Rouen fell without relief. | Lost men come back slowly (recruiting needs gold and villages; [LOGGING.md](LOGGING.md)); lords captured leave the war. A holding can be retaken. |
| 2 | A siege is a clock: relieve with a stronger force, or let it go (or strike the enemy's lands to pull the besiegers home). | Lincoln 1217 and Orléans 1429, relieved; Calais 1347, where the relief looked at the English works and left; *On Skirmishing* 20 (invade back to pull the besiegers home); Nicopolis 1396 (besiegers caught by a relief army). | The game's sally rule: the garrison and the lords inside join a relief only above 1.5 times the besiegers' strength ([TOOLS.md](TOOLS.md)). A weak relief dies alone. |
| 3 | Battle on your terms; the side time works against must seek it. The battle odds are the measure of the edge: a win keeping most of your men while most of his fall; judge by what is left, not how long it runs. (measure since Oct 25) | Vegetius III.26 ("Good generals never engage in a general engagement except when opportunity offers, or under great necessity"), III.9 (a battle is settled "in two or three hours"; when superior, "let him be not slow"); the debate above. | Strength is shown everywhere (reports, facts, muster); the agent can weigh it. |
| 4 | Squeeze an invader you cannot yet fight: follow it, pick off its parties with small forces. | Vegetius III.3 (if they gather they starve, if they disperse they are beaten in detail); *On Skirmishing* 22; Normandy 1054, Jerusalem 1183, France 1373 (Gaunt's raid followed, never fought, wrecked by hunger). | Hunt, screen and defend tools; armies in the game starve and lose cohesion. |
| 5 | Overwhelm where you can win: all you can spare, not only enough; every other front keeps enough to hold, never less; blows on several fronts, one or none, by the enemies' strength; every army sized against the strongest enemy army in reach; march apart, meet at one place on one day. (rewritten Oct 23, softened Oct 24) | Edward III in 1346: while the king's own army campaigned to Crécy, the forces he had left held Aiguillon against John of Normandy and won at Neville's Cross; Mongols 1241 (Legnica and Mohi two days apart; separate columns meeting at set places and times, May 2015); Hattin as the warning against stripping every garrison. | gather, and join for far-off lords (0.6); the muster table shows what could be where, and when. |
| 6 | Strike the divided enemy. | Mortemer 1054, Varaville 1057, Pontvallain 1370 (after the English captains quarrelled and split), Alnwick 1174, Bouvines 1214. | Enemy lords and armies in view and last seen; engage, hunt. |
| 7 | Raiders: small forces, struck while they are alone. Raiding parties are met by small groups of lords that can crush them, never by the armies, which stay on their tasks; a raider is struck while it is at work on a village, or on the march only where it can be reached before it reaches its walls, its army or its allies. (small forces since Oct 25; struck alone since Oct 29, not on their way home) | *On Skirmishing* 9 (wait until the raiders, "rushing to get to the villages and gather as much booty as possible", are too far from their main body to fall back on it; fall on them "as they are scattered all around"); *On Skirmishing* as a manual for the frontier districts' own forces against the yearly raids (Dennis; McMahon 2016). See [below](#principle-7-corrected-oct-29-raiders-struck-while-they-are-alone). | A raid holds the raiders at the village, where they make no move to flee an attacker; on the march a raider runs from a stronger party it can outpace. |
| 8 | Armies live on food and purpose; idle armies break up. | Vegetius III.3 ("hunger is more savage than the sword"), III.26 ("improved by work, enfeebled by inactivity"), III.1 (over-large armies beaten by their own size); a Third Crusade death list (14 of 98 recorded as drowned, captured or killed; Gillingham); forty-day service; Saladin's army going home from Tyre. | The game breaks up an army idle about two days, out of cohesion, or half starving; every order carries a food rule. |
| 9 | Raid with a purpose. | Edward III and the Black Prince (the 1355 raid's report of towns that paid "the wages of a thousand men-at-arms"); Ouranos (raid a fortress' land before besieging it); raids rarely conquer (Knolles 1370, Gaunt 1373). | Raid tool; looted villages stop giving recruits. |
| 10 | Exploit victory, keep the lords together, leave no base. | *Strategikon* VII.B.12 ("a near-miss is still a complete miss"); Vegetius III.26 (a pursuit with scattered forces throws the victory away); Saladin after Hattin (Acre in about a week, Jerusalem by 2 October) and Tyre left as the Third Crusade's base. | After a won battle, enemy lords are captured and its holdings weakly held. |
| 11 | Lords are the sinews of war. | Gillingham: "the surest way to win a battle was to kill or capture the opposing commander"; Poitiers 1356 (King John captured); Hastings; Alnwick. | Every captured lord is a party gone; the report lists the realm's own. |
| 12 | Hold what you can support; strike where the enemy is absent. | Baybars 1265–71 (razed coastal places, kept inland Safed, passed by Acre and Tripoli, sealed Antioch's port and passes first); Saladin razing Ascalon; Richard turning back from Jerusalem, given "the improbability that the city could be long held even if it were taken" (Painter); Ain Jalut after Hülegü left. | The frontier list shows enemy holdings and garrisons in sight. |
| 13 | After a defeat, rally fast; then think long. | Vegetius III.25 ("human minds are carried away by success"); Kekaumenos WJ 15 (gather even a quarter of your men and strike that day); Basil II; Charles V; Jean de Bueil ("who endures, wins"). | Recruiting is slow; the council resets the realm's course after a loss. |
| 14 | Initiative with judgment. | Kekaumenos WJ 10 ("let most of your actions be daring"); the taunts of cowardice after 1183 and the gamble of 1187 (Gillingham; Kedar); Philip VI mocked by his own nobles in 1339. | The agent is asked what it can do to the enemy, not only what the enemy can do. |
| 15 | Read the enemy's aim from its road, not its errands; beware the feint. (Oct 16) | Edward III's letter of 1339 ("we held ever our road forward, our people burning and destroying commonly to the breadth of twelve or fourteen leagues"); *On Skirmishing* 8 (watch the dust and smoke to "form an estimate of their route of march") and 9 (keep watching the main body, "if it sets out on the road"); *Strategikon* IX.3 (some gather supplies "and others should follow along in regular formation"); Philip VI at Poissy, 1346. See [below](#principle-15-oct-16-reading-an-armys-aim). | Each enemy army's trail in the report (0.6.4): the agent sees the road, not only the hour's errand. |
| 16 | A besieger keeps eyes on the roads: warned in time, lift the siege, close the ways, or meet the relief on your ground. (Oct 17) | Kekaumenos xxvi ("You can't find out about the enemy except by having many scouts"); *Strategikon* VIII.1.34 (a besieging army "should keep sending out patrols"); Mountsorrel 1217 (siege raised on the scouts' word); Calais 1347 (the dunes and the bridge of Nieulay held); Antioch 1098 (foot kept the lines, the knights met the relief); against: Lincoln 1217 (the warning laughed at), Nicopolis 1396 (the bringers of the news flogged and cropped). See [below](#principles-16-and-17-oct-17-the-besiegers-eyes-and-the-weaker-sides-one-blow). | The siege order's alarm counts every enemy lord near or heading for the place (0.7.2); watch and shadow (0.7.1) put a lord on a road or on an army. |
| 17 | The weaker side's one blow: when time is on the enemy's side, pass the lesser prize by, mass on the one blow that decides, strike before his parts join; when time is on yours, wait. Time is on your side only while waiting costs him more than you; an enemy taking your places while you wait has time on his side, however strong you are. (Oct 17; the time test since Oct 25) | Edward IV 1471 (Coventry passed by, London, Barnet, the march of "xxx myle and more" to Tewkesbury, *Arrivall*); Lincoln 1217 (every castellan's knights gathered against the larger force; Wendover); *Strategikon* IX.1; Kekaumenos lxvi; Vegetius III.26 for the other side. | Big armies walk slowly and sieges take days (TOOLS.md, "What a siege costs in hours"); every detour is hours the enemy uses. |
| 18 | The enemy's main army first: when you can beat it, gather an overwhelming force and meet it in the field, or strike as soon as you hold the advantage if gathering all would take too long, rather than wait for it to sit down before a castle; lesser prizes wait, raiders go to small forces; then take his places quickly while he regroups. When you cannot yet beat it, principles 4 and 17. (Oct 25) | Saladin 1187 (the forces of Egypt and Syria gathered; Hattin, 4 July; Acre in about a week; Jerusalem by 2 October); Mohi, 11 April 1241, then Hungary overrun; "The Mongols preferred to deal with all field armies before moving deep into enemy territory" (May 2015). See [below](#principle-18-and-the-raiders-oct-25-the-enemys-main-army-first). | An AI realm's great army takes castle after castle while it stands (run 20261024-2110-C2: four in 34 days); the battle odds give each side's remnant. |
| 19 | Victual the castles before the siege: a castle lives on its lands and villages, raids cut that, a siege stops it, and hunger then takes a tenth of the garrison a day; stock the places on a threatened front while the roads are open, above all those whose villages are raided, and read their food beside their walls. (Nov 1) | Vegetius IV.7 (provisions into the strongholds before the siege); Calais 1346–47 and Rouen 1418–19 (taken by hunger); Kenilworth 1266 (gave up when its food ran out); Harlech 1294–95 (held, supplied by sea). See [below](#principle-19-nov-1-victual-the-castles-before-the-siege). | Castles at the fronts with no food after their villages were raided (Nov 1, run 20261029-1903-A); the supply order and the hunger warning (0.13.19, 0.13.20). |

## Principle 15 (Oct 16): reading an army's aim

**Oct 16:** In an army, I might deviate slightly off course on my way to an objective to recruit or gather supplies, or maybe to engage an enemy I find vulnerable. The AI should still be getting the information it needs to infer what my main objective is, and slight deviations should not have it readjust its main strategy. Of the ways proposed, I chose two together: each enemy army's trail in the report (version 0.6.4, [TOOLS.md](TOOLS.md#what-the-agent-reads-besides-the-tools)) and a doctrine line. The line is general (judge by the course over days, beware the feint); it says nothing of this war or of how I play.

**The research** (one pass, Oct 16; the texts opened and quoted, unless marked):
- **The sources say the parties range, not the army.** It was the detachments that turned aside every day, while the king or the emir and the formed body kept the road, so the principle says so:
  - Edward III to his council, 1 November 1339 (Avesbury, tr. Locke): "So we held ever our road forward, our people burning and destroying commonly to the breadth of twelve or fourteen leagues of country."
  - 1346 in Normandy, Northburgh's letter from Caen (Avesbury, p. 358): the king took his road, and the men of the host rode out robbing and destroying "v. od vj. lieus environ toutz les jours" (five or six leagues around every day; the Latin version in Murimuth says miles, so the unit is uncertain).
  - 1346 on the Seine, Wynkeley's letter (Murimuth, p. 215): Philip VI followed the English king "diebus singulis" on the other bank, breaking the bridges, while burning spread "in latitudine xx. milliarium in circuitu" (about 20 miles round). The 40 miles in modern summaries (Ayton, Rogers) could not be checked.
  - *Strategikon* IX.3: "All the men should not engage in pillaging, but if the opportunity for collecting supplies presents itself, some should go out gathering them, and others should follow along in regular formation."
  - *On Skirmishing* 10: the horsemen who raid "separate themselves from the foot soldiers and move on a few days' distance ahead ... The baggage train with the infantry and their equipment follows behind."
- **Watching the road:**
  - *On Skirmishing* 8 ("Shadowing and following an army"): from the dust and smoke the commander "should be able to form an estimate of their route of march and where they are likely to camp".
  - *On Skirmishing* 9: the general "should have figured out the places and villages which the enemy plan to attack", and leaves an officer "to keep an eye on the battle formation of the emir and to report back to him whatever he observes there, such as if it sets out on the road".
  - Kekaumenos WJ 9: "Make it your practice to learn daily where and how your enemy is situated, what he is doing, what he expects, how large an army he has, what he wants ..."
- **The feint, Poissy 1346:** the English reached Poissy on 13 August and found the bridge broken; the king's marshals burned Saint-Germain-en-Laye, Saint-Cloud, Boulogne and Bourg-la-Reine around Paris (Froissart, ed. Luce, III); on 16 August Edward crossed the Seine on the rebuilt bridge and marched north (Thompson, introduction to Avesbury, p. xlvi). Luce, from the documents: "Le roi de France tomba dans le piége": Philip posted his main force at the bridge of Antony, south of Paris, at the very moment the English crossed.
- **Caveats:**
  - Froissart puts Philip at Saint-Denis, north of Paris; the Antony position is Luce's, followed by a modern guide (Gumm). Calling the raids a deliberate feint is Luce's reading: Rogers and Ayton argue Edward wanted a battle on ground of his choosing. That Philip was out of place when the English crossed is solid.
  - Vegetius III.6 is about keeping one's own march safe and secret, not reading the enemy's route; its closing line (learn the enemy's "present and future plans", catch them "seeking fodder and food") and III.10 (attack "those who loiter at a distance from their people for fodder or plunder") belong with principles 4 and 6, not here.
  - "Comes nearer a new prize than the old", from the first draft, has no source, and was left out.
  - Gaunt's 1373 raid and Normandy 1054 were not checked for this principle.

## Principles 16 and 17 (Oct 17): the besieger's eyes, and the weaker side's one blow

**Oct 17,** after the siege of Hollin Gate (Naglur's army stormed into the castle's defence while about 50 of Arnor's lords came to its relief one by one, [CHANGELOG.md](CHANGELOG.md)): the rest is more of a siege doctrine question. The agent should (1) post scouts around the army to figure out if a relieving force is coming during the siege so it can call it off and (2) understand that when the enemy is stronger, it is important to strike hard and quick, sometimes forgoing smaller targets in favor of guaranteeing a more important hit. The two lines are general, with their history; neither names this war, a place, a lord or an order. Principle 2's last sentence ("when you besiege, watch the roads, and leave before a stronger relief arrives") now points to principle 16, which carries Nicopolis.

**The research** (one pass, Oct 17; the texts opened and quoted, unless marked):
- **Principle 16:**
  - Kekaumenos xxvi (WJ 9.29–30): "You can't find out about the enemy except by having many scouts"; xxiv: "Without scouts it's impossible to get any work done."
  - *Strategikon* VIII.1.34 (Dennis, p. 82): "An army, no matter how strong, besieging a city must never leave its own camp unguarded, nor should it think that fortifications are sufficient to assure its safety, but it should keep sending out patrols." IX.5 (p. 104): patrols "in more than one direction ... so that if the enemy ... manages to elude one of the patrols, they will run into others." *Campaign Organization* 24 (Dennis, *Three Treatises*, p. 313): when the enemy attack from outside, "the siege operations may be slowed down, making sure, however, to keep the camp and the artillery securely guarded by a large enough force."
  - Antioch, 9 February 1098: Bohemond, "we are not so great that we can fight on two sides ... let a portion of the foot soldiers remain together to guard the pavilions ... Let the other portion, however, consisting of knights, go with us to meet our enemy" (*Gesta Francorum*); Raymond of Aguilers: "it was decided that the foot soldiers should guard the camp and the knights should go out." In May 1098 "messengers began to come very frequently, saying that aid was coming to the enemy" (Raymond), and the city was taken on 3 June, just before Kerbogha came.
  - Mountsorrel, May 1217: the besiegers, "being informed of this by their scouts, raised the siege, and retreated to the castle of Nottingham, where they determined to watch the progress of their approaching enemies" (Roger of Wendover II, p. 391).
  - Calais 1347: Edward guarded the dunes and "one bridge, called the bridge of Nieullet"; Philip's marshals, "after they had well examined all the passes", told him there was no way through "but with infinite loss of men" (Froissart, tr. Johnes, I.144). Philip withdrew on the night of 1 August; Calais surrendered on the 3rd (encyclopedia, after Sumption).
  - Lincoln, 20 May 1217, against: the besiegers, "when their messengers told them of the approach of their adversaries ... only laughed at them, and continued to hurl missiles from their mangonells" (Wendover II, pp. 393–94).
  - Nicopolis 1396, against: the crusaders were "ignorant ... of their approach" (Froissart, tr. Johnes, IV.81); the Religieux de Saint-Denys (II, XVII.25) has the marshal Boucicaut, told repeatedly by foragers who had met the Turks, have "some flogged and some of their ears cut off" ("quosdam affligi fecerat, quosdam exauriculari") as men who would "terrify the army with your empty words". Modern retellings (Tuchman) soften it to a threat.
- **Principle 17:**
  - Edward IV, 1471 (*Historie of the Arrivall*, ed. Bruce): he landed with "vc well chosen men", and Warwick "had assembled greatar nombar than the Kynge"; at Coventry, where Warwick's party "dayly grew and encreasyd", it was not "behoffoll to assayll, ne to tary for the asseginge therof ... it was thowght more expedient ... to draw towards London"; against Margaret "his entent was to encountar them as sonne as he myght ... that they shuld assemble no myght owt of eny contrye but where they then were", and on 3 May he marched "xxx myle and more" on a "right-an-hot day ... havynge allway good espialls upon them". Barnet was 14 April, Tewkesbury 4 May.
  - Lincoln 1217: the Marshal "convoked all the castellans belonging to the king, and the knights who were in charge of castles in different parts of the kingdom", about 400 knights and 250 crossbowmen, against a force Wendover puts at 600 knights and "more than twenty thousand" (II, pp. 389–92).
  - *Strategikon* IX.1 (p. 93): attacks "carefully planned and swiftly carried out" work against "vastly superior" forces; "strike at the enemy before they can get themselves ready, especially if ... their forces are stronger than ours" (it still prefers surprises and tricks to a pitched battle). Kekaumenos lxvi (WJ 23.23ff): delay "when a reinforcement ... is coming to support you"; with none coming and supplies short, "don't delay at all".
  - **The writers disagree,** so the principle says when it holds: Vegetius III.26 (tr. Clarke): "It is much better to overcome the enemy by famine, surprise or terror than by general actions"; III.9, delay when the enemy's army will waste away; *Strategikon* VIII.2.4, "never be enticed into a pitched battle". The common ground is principle 3's question: whose side is time on?
- **Caveats:**
  - Neither Antioch source names Ridwan of Aleppo (the historians' identification); the warning came as reports, and Bohemond sent scouts once out.
  - Calais is about holding the roads more than scouting; Froissart's 200,000 French is fantasy.
  - Wendover's numbers for Lincoln are suspect; that Louis was away at the siege of Dover is from scholarship (encyclopedia), not Wendover; the sea fight of August 1217 also came before the peace.
  - Kerbogha's three weeks at Edessa letting Antioch fall first (Runciman) is scholarship only, and disputed (Brosset 2024); left out.
  - The *Strategikon* and *Three Treatises* wording was checked against a scanned copy (print editions; no free authorised text). Acre 1189–91 and Neuss 1474–75 were not checked; Vegetius Book IV was not read (the free translation stops at Book III); no free text of the *History of William Marshal*.

## The persona's eight lines, and their grounding

- **Counting before moving:**
  - *On Skirmishing* opens with watch posts and spies;
  - the Mongols decided a campaign in council on gathered intelligence (May 2015);
  - Kekaumenos WJ 9: "You can't find out about the enemy except by having many scouts."
- **Battle only with the edge, or when forced:** Vegetius III.26.
- **Strike without delay when superior:** Vegetius III.9; Kekaumenos WJ 10.
- **Strike the divided enemy:** Mortemer and Varaville (Gillingham, "William the Bastard at War").
- **Let the invader burn out, when you cannot yet beat it in the field** (the condition since Oct 25: the French refused battle after Crécy and Poitiers had shown they could not yet win it):
  - Charles V and du Guesclin, 1369–80: the king's ruling that Knolles' army "should not then be fought with";
  - Pontvallain;
  - Gaunt's raid of 1373 followed to Bordeaux;
  - by 1375 England held only Calais, Cherbourg, Brest, Bordeaux, Bayonne and a few forts.
- **March apart, strike together, armies before castles:** Legnica (9 April 1241) and Mohi (11 April); "The Mongols preferred to deal with all field armies before moving deep into enemy territory" (May 2015).
- **Strike where the enemy is absent:** Ain Jalut, 3 September 1260, after Hülegü left with most of the army (Amitai).
- **Exploit, and leave no base:** Saladin after Hattin, and Tyre (Gibb, Baldwin).
- **A ninth line since Oct 23, reworded Oct 24, all that can be spared brought to the blow and each other front left enough to hold:** 1346, below.

## Principle 5 rewritten (Oct 23): one main effort (softened Oct 24, below)

**Oct 23**, after run C2: we should also design the agent's system prompt to make it more focused in its strategy. In C2 the kingdom's agent ran five to eight armies at once and lost three of them piecemeal to Dol Guldur's one army of 20 lords. My reason was a general rule of war: one front gets the weight of the realm, the others are held with less, rather than every front treated alike. It is written in the doctrine's own words, the same for every agent, and nothing in it names this war's fronts, armies or places.

**What the read of runs C1 and C2 found** (CHANGELOG, Oct 23): the old principle 5 said "Concentrate", but three lines pulled the other way:
- the council's step 3, "Give every lord a part in it";
- "At a poke, decide what this poke needs". So the agent answered raiders and sightings while the news of the enemy's main army went unanswered.
- principle 17, on massing for a single blow, applies only when the enemy is stronger. In C2 the kingdom was stronger overall, but each of its armies was the weaker side where it fought.

**What changed** (`agent/warroom-system.template.md`, and each realm agent's `warroom-system.md`):
- **Principle 5,** now "One main effort; hold the other fronts":
  - One front gets an army able to beat, by a wide margin, the strongest enemy army that can reach it. It destroys the enemy's armies there one after another, then turns to the next front.
  - Every other front gets a holding force: smaller, but enough to keep its castles and drive off its raiders. Its task is to keep that front as it is.
  - Every army is sized against the strongest enemy army that could reach it within a few days. Two armies in reach of one stronger enemy army join, or keep out of its reach.
  - The old lines stay: march apart and meet on one day; never strip every garrison.
- **Who you are:** a ninth line, "You put your weight on one front and hold the others with less", with 1346.
- **The council of war:**
  - Step 2 names the main effort and its army, what each other front needs, and who holds it.
  - Step 3 is now "Fill the main army first", then the holding forces, then the other lords' lesser tasks.
  - Step 4 writes the main effort and the held fronts into the plan.
- **Time:** a poke that a front's holding force can meet is met from that front's own lords, and the main army stays on its task. A poke with an enemy army stronger than any of the realm's, or one that can reach the main army, is answered for the whole realm, as a council would.

**What it rests on** (standard accounts of 1346, Sumption, *Trial by Battle*, 1990; not opened for this change):
- Edward III's own army landed in Normandy in July 1346 and won at Crécy on 26 August.
- Meanwhile Aiguillon, held for Henry of Lancaster in Gascony, kept John of Normandy's great army before its walls from April to August, until his father recalled it against Edward.
- On 17 October the northern lords and the Archbishop of York, left to hold the border, beat the Scots at Neville's Cross and took King David II.
- So one main blow was struck, two fronts were held by smaller forces, and both holding forces did their task.

**What would show it working:**
- A council names one main effort and its army, sized against the strongest enemy army in reach.
- Smaller holding forces elsewhere keep their fronts rather than seek battle.
- The main army stays on its task when raiders appear on another front.
- No army of the realm is met alone by a bigger enemy army.

## Principle 5 softened (Oct 24): overwhelm where you can win

**Oct 24**, on the Oct 23 wording: I don't like the wording on principle 5. It should be less strong, telling the agent to strategize such that, if it can win on a front with overwhelming force, it should concentrate overwhelming force there instead of just an adequate one, without taking so much from the other fronts that their forces become inadequate. This might work for multiple fronts, just one, or none, based on the relative strength of its enemies. As before, it is written in the doctrine's own words, the same for every agent, and names nothing of this war.

**What changed** (`agent/warroom-system.template.md`, and each realm agent's `warroom-system.md`):
- **Principle 5,** now "Overwhelm where you can win; keep enough everywhere else":
  - Where an enemy army can be beaten, the agent brings all it can spare, not only enough to win, so the battle is won by a wide margin, costs few men, leaves the enemy little to rally and the army whole for the next blow.
  - What it can spare is what the other fronts do not need. Each keeps enough to hold its places and turn back the enemies that can reach it, and is never taken below that: "a front stripped to feed another's battle is the next one lost".
  - No set number of blows: on several fronts against weak enemies, perhaps one against strong ones, none against enemies stronger everywhere, and then it holds and waits for its chance (principles 4, 6 and 17).
  - Kept from Oct 23: every army sized against the strongest enemy army within a few days' reach; two armies in reach of one stronger enemy army join, or keep out of its reach; march apart and meet on one day; never strip every garrison.
  - Gone: "choose one front for your main effort", the "holding force" whose task is "to keep that front as it is, not to win there", and "strength shared evenly between fronts is too weak to win on any of them".
- **Who you are:** the ninth line is now "You bring all you can spare to the blow, and leave each other front enough to hold", with 1346 told as the fronts that held while the king's army campaigned.
- **The council of war:**
  - Step 2 names what each front needs to hold and who holds it, and where a blow can be won, if anywhere, and with which army.
  - Step 3 is "Give every lord a part in it" again, in an order: first what each front needs to hold, then all that can be spared to the blows, then lesser tasks for any lords left.
  - Step 4 writes the held fronts and the blows into the plan.
- **Time:** a front's own lords meet what they can; striking armies stay on their tasks. An enemy army stronger than the lords facing it, or one that can reach one of the realm's armies, is answered for the whole realm, as a council would.

**What it rests on:** the 1346 account above, now cited only for the fronts that held. Crécy itself was won by the smaller army on ground of its choosing, so it is not cited as a blow struck with overwhelming force. That a wide win costs fewer men is the common rule of war; it was not checked against the game's own battle sums.

**What would show it working:**
- A council's plan says what each front holds with, and where it strikes, if anywhere.
- An army sent to strike is far stronger than what it meets, not just stronger.
- No front is left too weak to keep its places for another front's battle.
- Against enemies stronger everywhere, the agent holds and waits rather than forcing a blow.

## Principle 18 and the raiders (Oct 25): the enemy's main army first

**Oct 25**, after the read of run C2 on 0.11.5 below: I'm seeing a general pattern where the agent fails to prioritize engaging an enemy's main force and pays the price. Then, on the fixes offered: I'd add more to the main army part, something like this: prioritize your efforts properly: if you see an enemy's main force and you can beat them, you should plan on assembling an overwhelming force and meeting them on the field, or as quickly as possible so long as you have the advantage, and crushing them. Then you can be aggressive and quickly take over their territory as they aim to regroup. I'd also add some notes on the raiding parties: armies should not be spending time chasing raiding parties and should be focused on their primary objectives. Raiding parties should only be answered by smaller groups of lords that can crush them; the focus of large armies should not be diverted. As before, it is written in the doctrine's own words, the same for every agent, and names nothing of this war.

**What the read of run 20261024-2110-C2 found** (the kingdom's war room calls; CHANGELOG, Oct 25):
- From day 5 to day 39 the agent asked the battle odds against Dol Guldur's great army (Lorgath, 16,600 to 20,100 strength) 18 times in the open; every one said it would win, keeping 75 to 91% of its men. On day 5 the chase question said its army was 88% faster than Lorgath's. It never ordered an attack. Lorgath took Imnagath (twice), Framsburg and Dûglarshun meanwhile.
- 16 of its 46 council plans held the condition "strike him only while he is tied to a siege of one of our places"; his sieges ran half a day to five days and its armies stood four or five days away, so the moment never came within reach.
- Its main armies spent those days on raiders: 143 of 221 pokes were about raiders or lone enemy lords, 114 of the 386 orders in its poke answers went after them, and the 8 aimed at Lorgath all came on or after day 39. At day 32 the realm had 50,000 strength in the field against his 18,000; the most it ever had near him before Bagbûrz was 12,500.
- **The lines that pointed that way:**
  - principle 2 and 3's "tied to a siege";
  - "the edge" with no measure, so a forecast keeping 79% of its men read as "narrow";
  - the persona's du Guesclin line ("let an invader burn itself out, destroy its detachments ... without the battle"), with principles 4, 6, 7, 11, 12 and 15 all aimed at the enemy's parties;
  - principle 17's "when time is on your side, wait" (its plans expected the great army to break up within 8.8 days on day 25, within 10 on day 32; it stayed whole until about day 50);
  - council step 3, every front's share first.
  Only one line pointed at the main army: "you break the enemy's field armies before you take its castles".

**What changed** (`agent/warroom-system.template.md`, and each realm agent's `warroom-system.md`):
- **Principle 18, new: "The enemy's main army first."**
  - Its main field army counts most: while it stands it takes the realm's places one by one and shields everything the enemy holds; once it is broken, his castles and lands lie open.
  - When the agent finds it and can beat it, its destruction is the first aim: gather an overwhelming force and meet it in the field, or, if gathering everything would take too long, strike as soon as it holds the advantage, rather than wait for it to sit down before one of its castles, and crush it.
  - Lesser prizes wait; raiders go to small forces (principle 7).
  - Then be aggressive: take his places quickly while he regroups, before his lords gather again (principle 10).
  - When it cannot yet beat it: hold, squeeze and wait (principles 4 and 17).
- **Principle 7, "Raiders: small forces, and on their way home":** raiding parties are met by small groups of lords strong enough to crush them, never by the armies; an army chasing raiders is away from its task while the enemy's main army gains; an army turns aside only for an enemy force worth more than its task. The road-home lines stay.
- **Principle 4:** its raiding and foraging parties are picked off "with small forces".
- **Principle 3:** the battle odds are the measure of the edge: a win keeping most of your men while most of his fall; battles here can last a day or more, so judge by what each side has left, not by how long it runs. (Vegetius' "two or three hours" stays.)
- **Principle 17:** time is on your side only while waiting costs him more than you (his army hungry or breaking up, his allies leaving); if he goes on taking your places while you wait, time is on his side however strong you are; the stronger side reads principle 18.
- **Who you are:** "When you cannot yet beat an invader in the field, you let it burn itself out ..." (the condition is new), with "after Crécy and Poitiers, the French could not yet win it".
- **The council of war:** step 2 begins with where each enemy's main army is and whether it can be beaten (principle 18); step 3's lesser tasks name "small groups of lords against raiders". Step 3's order (the fronts' needs first, then the blows) is mine of Oct 24 and stays.
- Kept as they were: principle 5 (my softened wording), principle 2, principle 6 (the divided enemy, which covers a detached army worth an army's turning aside).

**What it rests on** (standard accounts and the earlier passes' sources; not opened again for this change):
- Saladin, 1187: he gathered the forces of Egypt and Syria, drew the Kingdom of Jerusalem's field army out by besieging Tiberias, destroyed it at Hattin on 4 July, took Acre within about a week and Jerusalem on 2 October (principles 1 and 10 above, Gibb and Baldwin in Setton).
- The Mongols, 1241: the Hungarian king's army destroyed at Mohi on 11 April, the kingdom east of the Danube overrun after; "The Mongols preferred to deal with all field armies before moving deep into enemy territory" (May 2015, the persona's line above).
- *On Skirmishing* as the frontier districts' manual against the yearly raids, met with their own local forces (Dennis, *Three Byzantine Military Treatises*; McMahon 2016).
- Charles V's refusal of battle after Crécy (1346) and Poitiers (1356, King John captured): the persona's grounding above.
- **Caveats:** Hattin was won by the larger army, but on ground and water Saladin chose, so it is cited for the blow and what followed, not as a measure of how much larger. Mohi's numbers are unknown; it is cited for the order of things, army first, land after. The edge measure in principle 3 is the game's own forecast, not a medieval rule.

**What would show it working:**
- A council names each enemy's main army and says whether it can be beaten; when it can, the plan gathers against it and meets it in the field, without waiting for a siege.
- When the battle odds give a win keeping most of its men, the agent strikes rather than calling it narrow.
- Raiders are met by small groups of lords; the armies do not leave their tasks for them.
- After the main army is broken, the agent takes the enemy's places quickly.
- Against a main army it cannot beat, it still holds and waits (principles 4 and 17), and no army of the realm meets it alone.

## Principle 7 corrected (Oct 29): raiders struck while they are alone

**Oct 29**, reading the instructions: in the general system prompt, telling the AI to attack raiders "on their way home" is wrong in terms of the game mechanics. The smarter move is to attack them when they are isolated from their allies and we can either catch them before they get to safety or they are already bogged down in a raid, which means they are stuck, isolated, and therefore in a vulnerable position. As before, it is written in the doctrine's own words, the same for every agent, and names nothing of this war.

**Why the old line was wrong here:**
- It applied *On Skirmishing* 4 (Dennis, p. 159) to raiders in general. That chapter is about the whole invading host coming back through the frontier passes, "burdened with a lot of baggage, captives, and animals", tired and "anxious to get back to their own country", after days in which the themes could gather their infantry at the defiles. Leo Phokas' ambush of 950, also cited for the old line, is that case: ʿAli, "encumbered by many prisoners and vast amounts of booty", caught in the narrow defiles on his way home (Dennis' note, p. 157). *Taktika* 18.128, cited too, was not opened again.
- In the game a raider does not turn for home with its plunder: when a raid ends, its leader picks its next aim as every lord the game runs does.
- What the game gives instead is the raid itself (read in the 1.4.8 source, Oct 29):
  - A raid is a battle at the village (`RaidEventComponent`): first the village's militia, then the looting, until the village's health runs out or the raiders are beaten. The report gives the looting its clock (`Forecast.RaidHoursLeft`, 0.7.5).
  - A party in such a battle makes no quick moves (`MobilePartyAi.TickInternal` returns at once while `MapEvent` is set), so it makes no move to flee an attacker, as it would on the march; an attack on it joins the raid's battle (`StartBattleAction`).
  - Its leader still decides every hour during a raid (`AiPartyThinkBehavior.PartyHourlyAiTick`) and ends the raid if another aim scores better, so the raid holding it there is the rule, not a certainty.
  - On the march, a lord the game runs flees a stronger party it can outpace (`MobilePartyAi`'s flight rule), so a raider is caught there only by a hunter that reaches it before it reaches its walls, its army or its allies; the `can_catch` question answers that.
- An agent had found it on its own: the kingdom's day-1 lesson in run 20261024-2110-C2, "Dol Guldur's lords (speed 3.1) outpace our big armies: we only catch them when they stop to besiege or raid" (CHANGELOG, Oct 25). The same read found that lesson carried over to the slow main army as "strike him only while he is tied to a siege", so the new lines name raiders only, and principle 18 still says not to wait for a main army to sit down before a castle.

**What changed** (`agent/warroom-system.template.md`, and each realm agent's `warroom-system.md`):
- **Principle 7,** now "Raiders: small forces, struck while they are alone":
  - kept (Oct 25): small groups of lords strong enough to crush them, never the armies; an army turns aside only for an enemy force worth more than its task; never chase a raider into the arms of its main army;
  - new: a raider is weakest cut off from its own, with *On Skirmishing* 9; strike it while it is at work on a village, where the raid holds it and it does not flee as it would on the march; on the march, only if it can be reached before it reaches safety: its own walls, its army or its allies;
  - gone: "Raiders loaded with plunder are slow and careless", the "return home unscathed" quotation, and "Strike them on their road home, not in the strength of their first advance".
- Already in line and unchanged: principle 4 (pick off its raiding parties with small forces), 6 (raiders scattered over your villages are the divided enemy), 9 (raiders who scatter far from support feed the enemy's army), 18 (its raiders are met by small forces), and the council of war's "small groups of lords against raiders".

**What it rests on:** *On Skirmishing* 9 (Dennis, pp. 171–73, opened in the scanned copy for this change). The general watches from hiding as the enemy "move out for attack and scatter for plunder"; "When the troops going out to raid have gotten far enough away from the emir's battle formation so they cannot retreat to it again ..., since each man will be rushing to get to the villages and gather as much booty as possible", he attacks the emir's weakened line; if it is too strong, he goes "with due speed" to "the enemy soldiers who are dispersed about. During the whole day he should charge in upon them and fight them, as they are scattered all around".

**Caveats:**
- The chapter's first choice is the main body, weakened by the raiders' absence; the principle takes its second, the scattered raiders, because it gives raiders to small forces and keeps the armies on their tasks. Principle 6 covers the main body divided.
- Chapter 4 is not wrong for its own case, a whole host coming home through passes the defenders hold; but that is a host, not a raider, and the doctrine still has no line on crossings (my choice, Oct 16).

**What would show it working:**
- Small groups of lords sent at raiders already at work on a village, or at raiders they can reach before they get back among their own.
- Fewer hunts that end with the raider gone or back among its own.
- No lord drawn after a raider into its main army, and no army waiting for an enemy main army to stop, as with the raiders.

## Principle 19 (Nov 1): victual the castles before the siege

**Nov 1.** I saw a lot of castles at my fronts with zero food due to raids on their villages. The supply order came out of it (0.13.19, docs/TOOLS.md). Proposed among three extras: "A rule of thumb on stocking castles before a siege", written like the existing ones with historical examples, saying nothing about this war, with a note that the doctrine is my call. I took all the optional extras, to be implemented as well. It is in the doctrine's own words, the same for every realm agent, and names nothing of this war (no front, no place, no enemy).

**The game's side** (1.4.8, `DefaultSettlementFoodModel`; docs/TOOLS.md "Food for a holding"):
- A castle or town lives on its lands and its unraided bound villages.
- Under siege nothing comes in, and its people and garrison go on eating.
- Out of food, a tenth of the garrison falls to hunger each day. The game's storm test (`StartingAssaultOnBesiegedSettlementIsLogical`) weighs the defenders as they are, so a starving garrison is soon one a besieger storms.
- YOUR HOLDINGS gives each own castle's and town's food in days of siege (0.13.19). The hunger warning pokes when one is short with enemies near (0.13.20).

**The principle** (19): stock the places on a threatened front while the roads are open, above all those whose villages are raided; read their food beside their walls, since a place that would hold against any storm still falls once its food runs out.

**What it rests on:**
- **Vegetius, *Epitoma rei militaris* IV.7:** before a siege, bring the provisions into the strongholds, and leave nothing outside for the besieger.
- **Calais, 1346–47:** Edward III held it close for about eleven months. It surrendered to hunger once Philip VI's relief army had turned away without fighting.
- **Rouen, 1418–19:** Henry V blockaded it from the summer of 1418. The city, starving (it had put its poor and its useless mouths out into the ditch), surrendered in January 1419.
- **Kenilworth, 1266:** the longest siege of medieval England, from June to December. Its garrison held off every assault, and gave up only when food ran short and sickness spread.
- **Harlech, 1294–95:** cut off by land in Madog ap Llywelyn's rising. It held because it was supplied by sea, as Edward I's coastal castles were meant to be.

**Caveats:**
- The game has no blockade runner. A besieged place cannot be supplied (the supply order ends there), so the principle says "before the siege", and Harlech's sea road stands for the open road, not a way in.
- It is a rule for a threatened front, not for every place. The order spends a lord's time and half his gold at most, and principle 12 (hold what you can support) still decides which places are worth it.

**What would show it working:** supply orders to threatened castles before sieges begin, fewer sieges won by hunger against the agent's realm, and food lines in councils' notes.

## The game-fit sweep, and principles 20 to 29 (Nov 13)

**Nov 12.** On day 10 of the A2 tuning game both agents were holding back: the orcs had fought no battle with a lord and begun no siege, sitting on 11.8 million gold with most lords at 15 to 50% of their ranks; Dol Guldur chased one of my lone lords with Khamûl's slow army for 11 hours, concluded that armies cannot catch lone lords, and stopped there. I asked why it would not split the army into faster groups within reach of each other, tie my lords up in battle with the fast ones and bring the strong ones in, and then asked for a sweep that keeps what applies to the game from the historical principles, throws out what does not, and writes new principles from what we know of the game: fast lords hunting with reserves to turn the tide, slow lords as bait with reserves, more initiative and unexpected moves when needed, and a defence always looking for the counter-attack.

**How it was done.** Three agents worked in parallel: one checked every claim of the persona and the 19 principles against the decompiled 1.4.8 code and the TAOM 2.0.26 pack; one worked out the game's rules for joining battles, retreat, capture, pace, armies, sieges and raids, and principles built on them; one read my comments on the agents' strategy since August and my own play (my recorded decisions, battles in person, orders to my lieutenant, the test runs). Claude Code re-checked the rules the new principles lean on most in the game's code. The full review, with every verdict and the evidence, was published as the report "Doctrine Overhaul"; the working files are in the session's scratchpad (`doctrine-review\A-sweep.md`, `B-mechanics.md`, `C-player.md`).

**The game rules everything below rests on** (1.4.8 unless TAOM is named):
- A battle holds both sides: a party in a map event makes no decisions (`MobilePartyAi.TickInternal`). Field rounds every 30 minutes, storms every 60 (`DefaultCombatSimulationModel.GetSimulationTickInterval`).
- A side may retreat from a field battle only after 8 rounds, at morale 20 or less, having lost the last 4 rounds, at 0.2 a round (`DefaultEncounterModel.GetRunAwayChanceInternal`); never from a raid, a storm, a sally or a fight at a siege camp (`GetMapEventSideRunAwayChance`). A side that retreats loses no lords (`MapEvent.CaptureDefeatedPartyMembers` returns at once).
- Battle length (replayed from the game's rules, equal troops): lopsided fights 8 to 14 hours whatever the size; even fights about 16 hours at 100 a side, about a day at 1,000, several days at 5,000. A side a quarter to a third of its attacker holds about 6 to 8.5 hours, at half about 8 to 13; a reserve as strong as the attacker that arrives within 6 hours almost always wins, at 8 about one time in three, at 10 never.
- The computer's quick reactions look about 9 map units out (encounter radius 3 × (2 + 1)), every 9 to 10 minutes; a target's friends count within 6, in full within 3; attack needs a local advantage of at least 1 (`DefaultMobilePartyAIModel.GetBestInitiativeBehavior`, `CalculateInitiativeScoresForEnemy`). An army leader ignores a lone party it outweighs more than 2 to 1 unless waiting for its lords; lords marching to join an army skip every enemy weaker than that army (same method). No line-of-sight test. Hunting by the hourly decision only within about half a day of the hunter's holdings (`AiEngagePartyBehavior`).
- Pace: `4 × (200/(200+men))^0.4` with an army's men pooled on its leader (`DefaultPartySpeedCalculatingModel.CalculateBaseSpeedForParty`); loot within capacity costs at most 2% (`GetCargoEffect`), prisoners about 20% at one per man; winners are slowed 0.4 for 6 hours after a battle, a retreating side is not (`DisorganizedStateCampaignBehavior`).
- Armies: idle counter 48 hours, cohesion −2 − ¼ per party a day, computer leaders buy cohesion back with influence, a gathering army waits up to 72 hours (`Army.cs`, `DefaultArmyManagementCalculationModel`).
- Storms: the attackers' blows per round depend only on the defenders' number (`GetSimulationTicksForBattleRound`). Lords inside a besieged place are defenders, but the place's stores feed only its people and garrison (`DefaultSettlementFoodModel`: prosperity/40 + garrison/20); a lord inside eats what he carries, then buys from a town's market (`PartiesBuyFoodCampaignBehavior`), and a castle has none.
- Hunger wounds, it does not kill: 25% of a field party's regulars a day, 10% of a garrison's (`DefaultPartyHealingModel.GetDailyHealingForRegulars`); no foraging; the computer gathers no army and starts no siege under 12 days of food (`AiMilitaryBehavior.GetFoodScoreForActionType`).
- Sieges: the computer besieges only with about twice the wall-weighted defence, food under 100 halving the walls' part, and counts field enemies within about 6.7 of the gate (`DefaultTargetScoreCalculatingModel.GetTargetScoreForFaction`); TAOM makes Dol Guldur and Gundabad count themselves 1.75 times, Mordor and Isengard 2 times (`army_targeting.json`). The garrison sallies to join a relief at 1.5 times the besiegers, 2 times alone, own realm only (`SallyOutsCampaignBehavior.CheckSallyOut`). Defenders of a raided or besieged place gather about 3 off and strike at 1.2 times (1.05 for a village) (`MobilePartyAi.GetNearbyPartyDataWhileDefendingSettlement`); TAOM scores defending 1.6 times higher.
- Raids: `(√raiders + 5)/900` of a village's health an hour, about 60 hours for 100 raiders and 25 for 1,000; a looted village gives no food, tax or recruits for 9 to 17 days (`DefaultRaidModel`, `VillageHealCampaignBehavior`).

**What changed in the 19** (the full wording is in `agent/warroom-system.template.md`):

| # | Verdict | The game's reason |
|---|---|---|
| persona "let it burn itself out" | rewritten | Nothing burns a computer host out: it sets out fed and buys back cohesion. The C2 read above named this line among those behind the agent's waiting. |
| 1 | rewritten | No garrison surrenders: a place falls to a storm or to hunger. Lords inside a place that falls are taken. |
| 2 | rewritten | The camp cannot run or retreat (confirmed); the sally bar and its own-realm rule stated; "raid his lands to pull the besiegers home" could not be confirmed for computer armies, replaced by taking his thin places while his army is tied; nobody leaves a storm; storms paced by the defenders. |
| 3 | rewritten | Vegetius' "two or three hours" dropped; no fatigue, march or surprise term in the battle sums; battles long, joiners count, a retreating side keeps its lords. |
| 4 | rewritten ("Shadow an invader") | No foraging; hosts set out fed. Shadowing and picking off lone lords kept. |
| 5 | edited | Confirmed by the battle sums (winner's losses about 58% at 1.2:1, 22% at 2:1, 11% at 3:1); "within reach" now means within a battle's hours; strength, not men. |
| 6 | edited | The forage example replaced by an army still gathering and relief lords arriving one by one. |
| 7 | kept | Confirmed: a raid holds the raider, no retreat from a raid fight. |
| 8 | rewritten ("Armies live on purpose") | "Destroyed more by starvation than battle" and "loot makes it slow" dropped; the idle rule made exact; mass costs speed; gather from the lords near, then disperse (my pattern). |
| 9 | kept, numbers added | A looted village's 9 to 17 days; raid a castle's villages before the siege (Ouranos). |
| 10 | edited | Lords taken are out for weeks, those who got away back in days; after the main army falls, several places at once (my pattern after run A's day 10). |
| 11 | edited | The numbers; hunt strays with single lords, never armies. |
| 12 | kept, addition | The computer's twice-the-defence siege rule; never rest an army idle. |
| 13 | edited | The winners' wounded heal 5 to 15 a day; "the same day" through standing orders. |
| 14 | kept | |
| 15 | edited | The computer rethinks every 1 to 6 hours; the report names destinations; feints come from the human player. |
| 16 | rewritten | "The besieger is blind" dropped (a camp sees like any party); the advice kept for the true reason: hours of warning against days of siege and half a day of answer delay. |
| 17 | rewritten | "Let want wear him down" dropped; what wears a realm down here: lost places, burnt villages, lords in dungeons, idle armies. |
| 18 | edited | His siege of your place is the best moment only when your army already stands within reach. |
| 19 | kept | Wording: hunger lays men low; a hungry place draws sieges. |

**The new principles and what each rests on:**
- **20. Initiative is the default:** waiting needs a reason and an end; gold is not strength while ranks are thin. Rests on the game's armies not wearing out (above) and recruiting being bounded by ranks and wage caps, not by the clan's purse. Evidence: the orcs' A2 days; my wage caps lifted at almost every session.
- **21. Every defence carries its counter-stroke:** keep field lords outside within hours, out of the computer's 6.7 count; strike the camp before its engines are built; meanwhile take what he left thin. Inside, only the lords the defence needs, each lengthening the wait before a storm and the storm itself, but living on his own food (my correction, Nov 13: leave in only what maximises the holdout without asking too much of the food; in the game the lords' own food and a town's market are what they draw on, not the stores). Rests on the siege-choice count, the camp's no-retreat rule and the sally bar. Evidence: run A's day 10, when my relief of a castle held by its lords destroyed Dol Guldur's main army (8,301 of 8,327 lost). Worded "hours, not days" so it does not bring back C2's waiting for a siege.
- **22. Fix with the light, strike with the heavy:** my idea of Nov 12. Rests on the battle hold, the hold times and the 9-unit look. The reserve order carries it.
- **23. The bait and the hidden reserve:** a lone lord takes any bait he outweighs, an army only one of half its strength or more; the reserve beyond the 6-unit count and the 9-unit look but within about three quarters of the bait's hold time. A defended place is the strongest bait (my pattern).
- **24. March divided, fight united:** parts about 60% faster than one army (five lords of 300 at about 2.8 against one army of 1,500 at about 1.7); each part able to hold until the others arrive. The Mongol columns of 1241.
- **25. Strike them where they cannot get away:** the no-retreat situations and the no-capture-after-retreat rule. Lincoln 1217.
- **26. Catch them while they gather:** the marching-to-army skip and the 72-hour wait. Edward IV, 1471.
- **27. Speed decides who can force a battle:** cut-off, not race (my Nov 1 catch of a fleeing army after it raced for a castle); lone lords shadow armies because of the army 2:1 skip.
- **28. Draw them in pieces with a prize:** the defenders' gathering and 1.2/1.05 bars; TAOM's 1.6.
- **29. Ring the prize before the blow:** lords sent ahead with the harass order (0.19) keep near the place in a group within reach of each other, hunt its strays, burn its villages, then screen the camp and join the storm once the army besieges it. Harfleur 1415; Ouranos on raiding a fortress's land before the siege.
- **The human player** (in the instructions' own section, not numbered): never let fear of him alone turn down a fight the odds, judged by his record, say is won; his host is mostly called lords that scatter home after; strike them then, his places when he is elsewhere, the front he does not lead. Evidence: Dol Guldur's "I will not offer battle" and its refusal of a relief its forecast called a win (Nov 11).

**Beside the principles:** a "How battles are joined" section of facts (the rules above in plain words, no advice), the instructions' "Command through orders" naming the reserve and harass orders as the reflexes that must be in place before they are needed, and the lieutenant's instructions naming both orders and the battle facts in two lines.

**What would show it working:** fewer days without a battle or a siege while ranks and gold allow one; armies formed hours before a blow rather than kept idle; lords sent ahead to harass a place before an army sits down beside them; reserves posted behind a lord who starts a fight; strikes on lords marching to join an army; a defence that names its counter-stroke.

## What was left out on purpose

- **This war's plan, as I described it:** mass one great army, beat Aragorn's host, take the places that hold the most captured orc lords to free them, rebuild, then counter-attack with everyone.
  - The doctrine states the general principles I now explicitly want available: no doomed castle needs a hopeless defence; overwhelming force where a win is open, with enough kept on every other front (since Oct 23, softened Oct 24); the enemy's main army first when it can be beaten, raiders left to small forces, and his places taken quickly after (since Oct 25, my own general rule, in the doctrine's words); raiders struck while they are cut off from their own, at work on a village or caught before they reach safety (since Oct 29, my correction, in the doctrine's words); win with the edge; exploit; retake.
  - Whether, when and against whom to do any of it is the agent's to decide.
- **Freeing one's own captured lords by taking the place that holds them,** although the third research pass recommended it (Guy freed for Ascalon; Prince Leon's ransom paid in forts). It is part of the plan above. The agent sees where its captured lords are held, grouped by place; what it does with that is the test.
- **Diplomacy maxims** (sow discord among allies, accept peace on good terms, Kekaumenos on paying tribute): the agent has no diplomatic orders yet. They come with the coalition (plan, week 3).
- **Battlefield tactics** (the feigned retreat, formations, the charge at Arsuf): the game fights the battles.
- **Ransom and the killing of prisoners:** not in the agent's orders.

## What would show it working

These are signs to watch for in my standard test: load the Summer 9, 1087 save, then march on Western Moria with more than 5,000 men. They are not instructions to the agent.
- Its council, after the first look, reads the balance of strength (its field armies against the host coming) and sets a course for the whole realm, not one place.
- It does not feed a relief to a besieged place lord by lord, below the strength that would make the garrison sally out.
- It masses before it strikes, or refuses battle and harasses, and it strikes when the enemy divides or wears out.
- After a win, it follows up at once.
- When my army turns aside for a few hours (a village, a weak lord) and comes back to its road, the agent's plan holds; when the army takes a new road, the plan changes (principle 15).
- When it besieges, it keeps lords out on the roads a relief must come by, and lifts the siege, or meets the relief, when a stronger one is reported (principle 16).
- When the enemy is stronger overall, it does not spend its main army's days on small prizes on the way to the blow that matters (principle 17).
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
- Edward III's letter of 1 November 1339, from Robert of Avesbury, tr. A. A. Locke (1913): https://www.deremilitari.org/RESOURCES/SOURCES/avesbury.htm
- Adam Murimuth and Robert of Avesbury, *Continuatio chronicarum* and *De gestis mirabilibus regis Edwardi Tertii*, ed. E. M. Thompson (Rolls Series, 1889), with Northburgh's and Wynkeley's letters of 1346 and Thompson's introduction: https://archive.org/details/admurimuthconti00robegoog
- Froissart, *Chroniques*, ed. S. Luce, vol. III (1872), with Luce's summary of chapter LIX: https://www.gutenberg.org/ebooks/72385
- Hattin chronicle extracts (*De Expugnatione*, Ernoul, Imad ad-Din, Baha ad-Din), Fordham sourcebook translations: https://sourcebooks.fordham.edu/source/1187ernoul.asp
- *Gesta Francorum*, tr. A. C. Krey (1921): https://sourcebooks.fordham.edu/source/gesta-cde.asp ; Raymond of Aguilers, tr. A. C. Krey (1921): https://sourcebooks.fordham.edu/source/raymond-cde.asp
- Froissart, *Chronicles*, tr. T. Johnes (1848), 2 vols: https://archive.org/details/chroniclesofenglandjohnes1 , https://archive.org/details/chroniclesofenglandjohnes2
- *Chronique du Religieux de Saint-Denys*, ed. and tr. L. Bellaguet, vol. 2 (1840): https://archive.org/details/chroniquedureli02bellgoog
- *Historie of the Arrivall of Edward IV*, ed. J. Bruce (Camden Society, 1838): https://archive.org/details/historieofarriva01brucuoft
- G. T. Dennis, *Three Byzantine Military Treatises* (Dumbarton Oaks, 1985), *Campaign Organization*: https://www.doaks.org/resources/publications/books/three-byzantine-military-treatises
- Vegetius, tr. J. Clarke (1767), Brevik etext: https://archive.org/details/flavius-vegetius-renatus-flavius-vegetius-renatus-the-military-institutions-of-t

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
- Brosset, T., in *Al-Masāq* 36 (2024), on Kerbogha at Edessa: https://doi.org/10.1080/09503110.2024.2342205
- Gumm, I., "The Battle of Crécy" (In the Footsteps, battlefield guide): https://www.inthefootsteps.com/battle-of-crecy.pdf ; Madden, M. M., *The Black Prince at War* (PhD thesis, Minnesota, 2014), read for the 1355 raid's width and spies, not cited in the principle: https://hdl.handle.net/11299/171443
- Torres, O., "Los consejos de al-Harawi sobre estratagemas de guerra" (2017): https://dialnet.unirioja.es/descarga/articulo/6429172.pdf ; Rech, W., "Christine de Pizan on the laws of war" (2024): https://academic.oup.com/lril/article/12/1/47/7682371
- Ambühl, R., *Prisoners of War in the Hundred Years War* (summary and review): https://www.medievalists.net/2010/04/prisoners-of-war-in-the-hundred-years-war-the-golden-age-of-private-ransoms/ , https://scholarworks.iu.edu/journals/index.php/tmr/article/download/18561/24674
- Georgopoulou, M., review of Kennedy, *Crusader Castles* (1997): https://scholarworks.iu.edu/journals/index.php/tmr/article/view/14466/20584
- Latham, A., and Brown, R. L., "'War, Cruel and Sharp': England's Grand Strategy during the Hundred Years' War" (2020): https://www.medievalists.net/2020/04/england-grand-strategy-hundred-years-war/

**Encyclopedia articles** (dates and figures only; most summarise Sumption and Curry): the battles of al-Fule, Hattin, Arsuf, Jaffa (1192), Poitiers, Pontvallain, Kleidion, Bouvines, Nicopolis, the Kalka and Evesham; the sieges of Tyre (1187), Acre (1189–91), Calais (1346–47), Harfleur, Rouen (1418–19), Orléans, Tournai (1340) and Dover (1216–17); Kerbogha; the Black Prince's raid of 1355 and John of Gaunt's of 1373; the Treaty of Brétigny; knight service; on en.wikipedia.org, read in October 2026.
