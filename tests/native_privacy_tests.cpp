#include "../native/company_privacy.h"
#include <cassert>
#include <map>
int main() {
    std::map<unsigned,int> simulation={{7,20},{8,30},{9,40}}, view=simulation;
    volatile long others[]={7,9,9};int calls=0;
    auto clear=[&](std::map<unsigned,int>* world,unsigned e){(*world)[e]=0;++calls;};
    ClearForeignMoneyPositions(&simulation,&view,8,7,others,3,clear);
    assert(view[8]==30 && view[7]==0 && view[9]==0 && calls==2);
    assert(simulation[7]==20 && simulation[8]==30 && simulation[9]==40);
    // Host's canonical player is local: its amounts survive; duplicate entries
    // and a stale local player accidentally in the foreign list cannot erase it.
    view=simulation;calls=0;volatile long hostOthers[]={7,8,8};
    ClearForeignMoneyPositions(&simulation,&view,7,7,hostOthers,3,clear);
    assert(view[7]==20 && view[8]==0 && calls==1);
    // A missing identity or oversized list fails closed without touching UI data.
    view=simulation;calls=0;ClearForeignMoneyPositions(&simulation,&view,0,7,others,3,clear);
    ClearForeignMoneyPositions(&simulation,&view,8,7,others,9,clear);assert(view==simulation && calls==0);
    ClearForeignMoneyPositions(&simulation,&simulation,8,7,others,3,clear);assert(calls==0 && simulation[7]==20);
    // Reload/reconnect with no foreign companies clears only a foreign canonical.
    view=simulation;calls=0;ClearForeignMoneyPositions(&simulation,&view,8,7,others,0,clear);
    assert(view[8]==30 && view[7]==0 && calls==1);
}
