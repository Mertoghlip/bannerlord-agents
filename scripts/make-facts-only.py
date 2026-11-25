# Usage: python scripts/make-facts-only.py agent
# Builds agent/facts-only/*.template.md from the doctrine templates: kept sections word for word, decided sections rewritten, doctrine removed.
import re, sys, os

A = sys.argv[1]  # the repo's agent folder


def read(n):
    return open(os.path.join(A, n), encoding='utf-8').read().replace('\r', '')


def sections(t):
    parts = re.split(r'(?m)^(## .*)$', t)
    return parts[0], [(parts[i].strip(), parts[i + 1]) for i in range(1, len(parts), 2)]


def overlay(base, ov):
    h, bs = sections(base)
    _, os_ = sections(ov)
    d = list(bs)
    for k, v in os_:
        name = k
        shown = k
        if '=>' in k:
            a, b = k.split('=>')
            name = a.strip()
            shown = '## ' + b.strip()
        idx = [i for i, (n, _) in enumerate(d) if n == name]
        if v.strip() == '(removed)':
            d = [x for x in d if x[0] != name]
            continue
        if idx:
            d[idx[0]] = (shown, v)
        else:
            d.append((shown, v))
    return h + ''.join(n + '\n' + v for n, v in d)


def edit(text, old, new):
    assert text.count(old) == 1, (old[:80], text.count(old))
    return text.replace(old, new)


sysT, conT, filT = read('warroom-system.template.md'), read('warroom-continuous.template.md'), read('warroom-files.template.md')
full = overlay(overlay(sysT, conT), filT)
S = {n: v.strip() for n, v in sections(full)[1]}
manual = dict(sections(sysT)[1])['## Writing your own tools'].strip()

out = []


def add(name, body):
    out.append(name + '\n\n' + body.strip() + '\n')


add('## Your goal', S['## Your goal'])

add('## What the game does', """
The game's own rules that bear on a war, read in its code or measured in play. Battles and marching are in How battles are joined, below.

- **Castles and towns.** A castle or town falls in one of two ways: stormed, once its besiegers can win the storm, or starved, once its food runs out. A castle's food comes from its lands and its villages: raids cut it off, a siege stops it altogether, and once the store is empty a tenth of the garrison is laid low each day. Lords sheltering in a place that falls are taken with it. A place just taken has a thin garrison, and its old realm's nearest lords come to win it back.
- **The game's lords and sieges.** The game's own lords besiege a place only when they are about twice what holds it, its walls counted; a place short of food looks weaker to them. A siege camp sees no farther than any party: a few hours' march by day, half that by night, less through forest. A camp cannot storm until its preparations are done, a day or more.
- **A relief.** A siege camp that a relief falls on cannot draw off, and fights to the end; if it is beaten, its lords are taken. The relief fights the whole camp outside the walls. The garrison and the lords inside join it only once they and the relief together are more than one and a half times the besiegers, counting only that realm's own lords, not an ally's.
- **A sally.** While a camp besieges a place, the garrison and the lords inside come out against it on their own once they and their realm's lords near the camp are more than twice the camp's strength; only the lords camped there count for the camp, not those on their way. Nobody leaves that fight once it has begun.
- **Lords and prisoners.** A lord taken loses his party at once and sits in a dungeon for weeks unless he escapes. A lord beaten but not taken is back within days with few men. An army whose leader is taken breaks up. Wounded men take weeks to heal.
- **Armies.** Separate lords march faster than the same men in one army, and cost no influence or cohesion; calling lords into an army costs influence for each of them.
- **Raids.** A looted village gives its lord no taxes, its castle no food and its notables no recruits for one to two weeks. A raided village draws its realm's lords to it.
- **Catching a party.** Only a faster party can catch one on the march; a slower one catches it only where it must stop.
""")

add('## Strategy reviews', """
Now and then you hold a strategy review instead of answering a wake: the message says STRATEGY REVIEW and why it sits. A review sits every in-game day: at the latest 24 in-game hours after the last, sooner at the hour you set with `"review_in"` (1 to 24 in-game hours after your answer), and at once after a big event (a holding won or lost, a war, a peace, one of your holdings besieged, an army formed, an offer). A wake can set `"review_in": 1` for a review within the hour. The first answer of each conversation is a review too. Its message opens with a digest from your files: your realm in sum, your money, every lord with no order, what became of your orders, your captive lords, and the question, with each enemy army in view and the strength you could gather where it stands within 24, 48 and 72 hours.

Every lord must hold an order of yours. The digest lists every lord with no order (lords freed, lords whose order ended, new lords): give each one an order in that answer; a group order may cover several. The war room asks again for those left out, and gives any still left its standing order (defend the nearest castle or town of yours, a weak lord one well back from any threat) until you give him your own; a lord without your order would follow the game's own AI.

Ask the question at every review: could you gather enough to beat each enemy army where it stands? `muster` says who could be there by when, and `battle_odds` with those who arrive in time (`how`: `strike` for a siege camp at one of your places) how it would go; answer it for each enemy army.

The whole realm is in your files: your shelf; each folder's changes page; your own pages; plan/plan-check (names in your plan that no longer fit, orders and watches it does not name) and plan/watches; the chronicle (chronicle/record, and your chronicler's day pages); lords/index, settlements/index, intelligence/sightings and realm/in-sum. A review answers like a wake, with any orders, watches and pages, and `"review_in"` for the next one.
""")

ask = S['## How you are asked']
ask = edit(ask, 'So what you watch is what you hear about at once: set watches where your war is decided.',
           'So what your watches cover is what you hear about at once.')
ask = edit(ask, "At a wake, read your shelf (shelf.md: the hour, how long your orders take to land, and what is new to you in each file) and the pages the wake concerns, together in one step, then answer. Before you order, know what changed since you last looked: the shelf names it, each folder's changes page lists it, and each page shows its own changes at its top; a page that changed since you last read it is marked ▲. Keep to about the number of wakes a day your shelf gives: each wake costs time while the game runs.",
           "Your shelf (shelf.md) gives the hour, how long your orders take to land, and what is new to you in each file; each folder's changes page lists what changed, each page shows its own changes at its top, and a page that changed since you last read it is marked ▲. Your shelf also gives the number of wakes a day to keep to: each wake costs time while the game runs.")
add('## How you are asked', ask)

add('## Your memory', S['## Your memory'] + '\n- Your notes carry over between conversations; what you keep in them is up to you.')

ans = S['## How you answer']
ans = edit(ans, 'An answer with `"orders": []` is a good answer when nothing needs changing.', 'An answer may give `"orders": []`.')
ans = edit(ans, 'Keep each page short and current, at most about 12,000 characters.', 'A page holds at most about 12,000 characters.')
ans = edit(ans, ' Write your plan\'s pages at a review when the campaign changes, or at a wake only when one of the events your plan named has happened (principle 17); an intelligence page when you learned something about that enemy; a lesson when it will change a later decision, beginning one from something that worked with "Worth copying:".',
           ' Write them when you choose.')
add('## How you answer', ans)

man = edit(manual, 'Our tools stay yours to order. When a plan of yours needs a way of acting that none of them has, write a tool of your own: a small state machine',
           'Our tools stay yours to order. You may also write tools of your own: a small state machine')
add('## Writing your own tools', man)

for n in ['## Checking a tool', '## Diplomacy']:
    add(n, S[n])

al = S['## Allies']
al = edit(al, "An unmarked letter comes with your ally's next wake, which may be up to a day away: mark a letter when your ally must change an order sooner than that.",
          "An unmarked letter comes with your ally's next wake, which may be up to a day away.")
al = edit(al, "- Do not write only to acknowledge or thank: each letter takes your ally's time, and the game runs while it reads.",
          "- Each letter takes your ally's time to read, and the game runs while it reads.")
add('## Allies', al)

lu = S['## Look-ups']
lu = edit(lu, 'When you need several pages or answers, ask for them all at once, in one step, rather than one after another: each step takes time while the game runs.',
          'Several pages or answers can be asked for at once, in one step; each step takes time while the game runs.')
add('## Look-ups', lu)

aw = S['## Asking the way']
aw = edit(aw, 'Each question costs a few seconds while the game runs, so ask only when the way matters to your decision.', 'Each question costs a few seconds while the game runs.')
aw = edit(aw, '; ask it before a hunt.', '.')
aw = edit(aw, ", so set it from the engagement's answer when a fight would be long, or lower it when only those already close should count.",
          "; the engagement's answer says how long a fight would last.")
add('## Asking the way', aw)

for n in ['## Recruiting', '## Money']:
    add(n, S[n])

add('## The human player', """
One enemy lord is commanded in person by a human player. The report names his party (YOUR ENEMY, THE HUMAN PLAYER) and marks it wherever it lists it.
- He commands his whole realm, not only his own party: his lords may hold standing orders as yours do.
- His battles are fought on the spot. When one begins, only the lords and bands within 3 of it, and the lords in the armies of its leaders, take part (3.75 while he is in a siege). The clock stands still until it ends, so nobody else arrives, however near in hours. A storm he leads or defends is fought by the siege camp and those inside alone. Battles without him last hours and take in lords as they arrive.
- Your lords sent at him close in to about 6 from him and wait, then strike together once those who could come within 4 hours are in position (an order's own attack does this by itself).
- In the battles he fights in person he has done about as well as a side a quarter stronger than his own: at even strength he has won, and against a side a quarter stronger than his the fight could go either way. Battles of his realm's lords without him are fought by the game's rules like any others. `look_up` on `the_human_player` has every battle of his your realm saw (each side's men, strength, lords and troops, and the losses) and what your realm saw him do.
""")

add('## How battles are joined', S['## How battles are joined'])

add('## Time', """
The game does not wait for you.
- It runs while you think: each answer reaches it some hours of game time after the picture you read; your shelf says how many.
- Each order's rules are read every in-game hour and act that hour. A single move ends when the lord gets there, and he then follows the game's own AI; an order with rules goes on until it ends.
- Every step you take, every page you read and every question you ask costs real time while the game runs.
- An answer with no orders leaves every order as it stands.
""")

ru = S['## Rules']
ru = edit(ru, '- Lords holding your orders do not flee by themselves: keep caution on, unless you mean them to stand.',
          "- Lords holding your orders do not flee by themselves; an order's caution setting makes them shelter from a stronger enemy (orders/common-settings).")
add('## Rules', ru)

wa = S['## Watches']
wa = edit(wa, "Watch from far. A condition that turns true only when a threat is at the gate wakes you too late, since your answer reaches the game many hours after the wake (Command through orders): set your conditions to turn true while a threat is still gathering or a day's march off, and leave what must happen at the gate to your orders' own rules.",
          "A watch wakes you the hour a condition turns true; your answer then reaches the game some hours later.")
i = wa.index('An army of yours on the offensive, watched as it advances')
j = wa.index('A castle on a threatened front, short of food:')
wa = wa[:i] + 'Another, on a castle short of food:' + wa[j + len('A castle on a threatened front, short of food:'):]
add('## Watches', wa)

ho = S['## Your handover']
ho = edit(ho, "your intelligence pages (each enemy you track; for the human player, your record of battles with him, the bar you judge him by and how sure you are of it, and his habits), and your notes (your lessons and what is worth copying, joint plans in flight and what you promised your allies)",
          "your intelligence pages, and your notes (joint plans in flight, what you promised your allies, and whatever else you keep)")
add('## Your handover', ho)

fi = S['## Your files']
fi = edit(fi, '- **notes**: your pages (lessons and what is worth copying, and whatever else you keep), and handover (your last handover).',
          '- **notes**: your pages (whatever you keep), and handover (your last handover).')
fi = edit(fi, "- **intelligence**: a page for each enemy you track, yours to write (what it does, its pattern, the threat, your read of its plan, how sure you are, the date, and what would prove you wrong, naming the watch that would tell you); sightings (in view now, seen earlier and the enemy armies' trails, ★ for those you track), enemy-holdings, human-player-record and chances-to-strike, kept by the game. Track the enemies that bear on your war, and drop a page when one no longer does.",
          "- **intelligence**: your pages on the enemies you track, yours to write; sightings (in view now, seen earlier and the enemy armies' trails, ★ for those you track), enemy-holdings and human-player-record, kept by the game.")
add('## Your files', fi)

hdr = sysT.split('\n## ')[0].rstrip()
system = hdr + '\n\n' + '\n'.join(out)
os.makedirs(os.path.join(A, 'facts-only'), exist_ok=True)
open(os.path.join(A, 'facts-only', 'warroom-system.template.md'), 'w', encoding='utf-8', newline='\n').write(system)
open(os.path.join(A, 'facts-only', 'warroom-continuous.template.md'), 'w', encoding='utf-8', newline='\n').write(
    "<!-- The facts-only instructions (Nov 24, docs/FACTS-ONLY.md): warroom-system.md already holds the continuous agent's sections, so this layer adds nothing. -->\n")
pointer = dict(sections(filT)[1])['## Writing your own tools'].strip()
open(os.path.join(A, 'facts-only', 'warroom-files.template.md'), 'w', encoding='utf-8', newline='\n').write(
    "<!-- The facts-only instructions (Nov 24, docs/FACTS-ONLY.md): warroom-system.md already holds the files agent's sections; this layer only points the manual for writing tools at the Orders file, as for every files agent. -->\n\n## Writing your own tools\n\n" + pointer + "\n")
print(len(system), 'characters;', [n for n, _ in sections(system)[1]])
