"""Run the complete production hash function; all engine values are fixtures."""
from pathlib import Path
import unittest
from lupa.lua52 import LuaRuntime

CONTENT = Path(__file__).resolve().parents[1] / 'mod/mpfever_1/content'


class HashRotationTests(unittest.TestCase):
    def setUp(self):
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        self.lua.globals().C = self.lua.execute((CONTENT / 'mpfever_common.lua').read_text(encoding='utf-8'))
        self.lua.execute('''
            flags={};os.getenv=function(k)return flags[k]end
            R={partCost={},loopErrors={}}
            R.contentHash=function()
                if fail then error("fixture hash failure")end
                local parts={}
                for _,k in ipairs({"conParams","edges","constructions","nodeConfigs","lines"})do
                    if not (R.hashSkip and R.hashSkip[k])then parts[k]=k end
                end
                return parts
            end
            api={type={ComponentType={ACCOUNT=1}},engine={util={getPlayer=function()return 7 end},
                getComponent=function()return {balance=100,loan=20}end}}
            extendedParts=function()end;gameTime=function()return 100 end;log=function()end
        ''')
        text = (CONTENT / 'mpfever_bridge.script.lua').read_text(encoding='utf-8')
        start = text.index('local function simHash(')
        end = text.index('\nend\n', start) + 5
        self.lua.execute('local hashShown=false;local hashCostsShown=0\n' + text[start:end] + '\nhash=simHash')

    def check(self, code):
        self.lua.execute(code)

    def test_three_checkpoints_cover_every_heavy_group_and_keep_cheap_parts(self):
        self.check('''local seen={};for n=1,3 do local p=hash(0,nil,n)
                assert(p.money=="100/20" and p.lines and p.time==100)
                for _,k in ipairs({"conParams","edges","constructions","nodeConfigs"})do
                    if p[k]then seen[k]=(seen[k] or 0)+1 end
                end
            end
            assert(seen.conParams==1 and seen.edges==1 and seen.constructions==1 and seen.nodeConfigs==1)''')

    def test_full_determinism_and_autotests_do_not_rotate(self):
        self.check('''local function full(p)assert(p.conParams and p.edges and p.constructions and p.nodeConfigs)end
            full(hash(0,nil));flags.MPFEVER_SAVE="fixture.sav";full(hash(0,nil,1))
            flags.MPFEVER_HASHROT="0";full(hash(0,nil,2))''')

    def test_hash_context_is_restored_even_on_failure(self):
        self.check('''local previous={custom=true};R.hashSkip=previous
            hash(0,nil,1);assert(R.hashSkip==previous)
            fail=true;assert(not pcall(hash,0,nil,2));assert(R.hashSkip==previous)''')


if __name__ == '__main__':
    unittest.main()
