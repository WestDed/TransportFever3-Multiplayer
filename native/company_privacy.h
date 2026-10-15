#pragma once
// Local UI projection only: caller passes the freshly copied GUI world, never
// the authoritative simulation world. No account balances/journal are touched.
template<class World,class Clear>
static void ClearForeignMoneyPositions(World simulation, World world, unsigned mine, unsigned canonical,
    const volatile long* others, long count, Clear clear)
{
    if (!world || world==simulation || !mine || !others || count < 0 || count > 8) return;
    unsigned seen[9]; int n=0;
    for(long i=0;i<=count;i++) {
        unsigned e=i<count?(unsigned)others[i]:canonical;
        if (!e || e==mine) continue;
        bool duplicate=false;for(int j=0;j<n;j++)if(seen[j]==e)duplicate=true;
        if(duplicate)continue;
        seen[n++]=e;clear(world,e);
    }
}
