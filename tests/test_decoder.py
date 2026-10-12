"""Data-only decoder regressions against actual Lua 5.2 production code."""
from pathlib import Path
import random
import unittest
from lupa.lua52 import LuaRuntime

CONTENT = Path(__file__).resolve().parents[1] / 'mod/mpfever_1/content'


class DecoderTests(unittest.TestCase):
    def setUp(self):
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        self.c = self.lua.execute((CONTENT / 'mpfever_common.lua').read_text(encoding='utf-8'))
        self.lua.globals().C = self.c

    def test_serializer_roundtrip_all_bytes(self):
        # Run in Lua so Python's UTF-8 conversion never masks byte preservation.
        self.lua.execute('local s=""; for i=0,255 do s=s..string.char(i) end; assert(C.deser(C.ser(s))==s)')

    def test_real_action_shape_with_sparse_arguments(self):
        self.lua.execute('''
          local x={fn="makeWorldBuildProposalCmd",uid="Client:1",at=60400,co=2,
            args={n=4,[1]={proposal={addedNodes={{entity=-1,comp={position={x=-150.5,y=7,z=1e-7}}}},addedSegments={}}},
                  [3]=false,[4]=true},refs={{k="node",id=60,p={x=10,y=10,z=0}}},label="Пример\\nпути"}
          local y=C.deser(C.ser(x))
          assert(y.fn==x.fn and y.args.n==4 and y.args[2]==nil and y.args[3]==false)
          assert(y.args[1].proposal.addedNodes[1].comp.position.x==-150.5)
          assert(y.refs[1].id==60 and y.label==x.label and y.co==2)
        ''')

    def test_decimal_exponent_and_finite_extremes(self):
        for token in ['0', '-1', '1.25', '-.5', '1.', '1e-30', '-1E+30', '1.7976931348623157e308', '2.2250738585072014e-308']:
            with self.subTest(token=token):
                self.assertEqual(self.c.deser(token), self.lua.eval(token))

    def test_strings_keys_and_nil_match_data_semantics(self):
        self.lua.execute('local t=C.deser("{[false]=true,[2]=nil,[1]=false,[\\"x\\"]=\\"\\\\009\\\\010\\\\034\\\\092\\"}"); assert(t[false] and t[2]==nil and t[1]==false and t.x==string.char(9,10,34,92))')
        self.assertEqual(self.c.deser(' \r\n{["s"]="text",}\t')['s'], 'text')
        self.assertEqual(self.c.deser("'quoted'"), 'quoted')

    def test_generated_trusted_roundtrips(self):
        # Differential oracle is used only on C.ser output made by the test, never on hostile inputs.
        self.lua.execute('''
          math.randomseed(90420)
          local function same(a,b)
            if type(a)~=type(b) then return false end
            if type(a)~="table" then return a==b end
            for k,v in pairs(a) do if not same(v,b[k]) then return false end end
            for k,_ in pairs(b) do if a[k]==nil then return false end end
            return true
          end
          local function gen(d)
            if d==0 then return math.random(-100000,100000)/37 end
            return {n=3,[1]=gen(d-1),[3]=gen(d-1),yes=math.random(0,1)==1,
                    text=string.char(math.random(0,255)).."\\\"\\\\Пример",empty={}}
          end
          for i=1,100 do
            local wire=C.ser(gen(3))
            local trusted=assert(load("return "..wire,"test oracle","t",{}))()
            assert(same(C.deser(wire),trusted))
          end
        ''')

    def test_expressions_and_code_are_rejected_without_compilation(self):
        self.lua.execute('load=function() error("decoder compiled input") end; loadstring=load')
        for token in [
            '(function() return 7 end)()', '(function() while true do end end)()',
            '{["x"]=(function() return 7 end)()}', '{[(function() return 1 end)()]=2}',
            '{["x"]=1+2}', '2^100', '"a".."b"', '{["x"]=os.execute("echo unsafe")}',
            'true or false', '_G', 'setmetatable({}, {})', '{}; return 1', '\x1bLua', '-- comment\n{}',
        ]:
            with self.subTest(token=token):
                self.assertIsNone(self.c.deser(token))
        self.assertEqual(self.c.deser('{["safe"]=42}')['safe'], 42)

    def test_malformed_values_fail_closed(self):
        tokens = ['', '{', '{["x"]=1', '{["x"]1}', '{["x"]=}', '{[nil]=1}', '{[{}]=1}',
                  '{["x"]=nil,["x"]=2}', '{[1]=1,[1.0]=2}', '{["x"]=1,["x"]=2}',
                  '{} garbage', 'truex', 'false0', 'nilz', '1e', '1e+', '+1', '--1', '1.2.3', '0x10',
                  'NaN', '1e309', '-1e309', '"unterminated', '"bad\\q"', '"bad\\999"',
                  '"bad\\"', '"raw\ncontrol"', '"raw\x00control"', '{x=1}', '{1,2}', '{[1]=2;;}']
        for token in tokens:
            with self.subTest(token=token):
                self.assertIsNone(self.c.deser(token))
        for value in [None, 1, True, self.lua.table()]:
            self.assertIsNone(self.c.deser(value))

    def test_depth_boundary(self):
        depth = self.c.DESER_LIMITS.depth
        wire = '{[1]=' * (depth - 1) + '{}' + '}' * (depth - 1)
        self.assertIsNotNone(self.c.deser(wire))
        self.assertIsNone(self.c.deser('{[1]=' + wire + '}'))

    def test_entry_boundary_and_aggregate_limit(self):
        limit = self.c.DESER_LIMITS.entries
        fields = ','.join(f'[{i}]=nil' for i in range(limit))
        self.assertIsNotNone(self.c.deser('{' + fields + '}'))
        self.assertIsNone(self.c.deser('{' + fields + ',[100001]=nil}'))
        # nil-valued fields still consume the budget, as do fields inside child tables.
        self.assertIsNone(self.c.deser('{["nested"]={' + fields + '}}'))

    def test_string_and_total_size_boundaries(self):
        limit = self.c.DESER_LIMITS.stringBytes
        self.assertEqual(len(self.c.deser('"' + 'x' * limit + '"')), limit)
        self.assertIsNone(self.c.deser('"' + 'x' * (limit + 1) + '"'))
        total = self.c.DESER_LIMITS.bytes
        self.assertIsNotNone(self.c.deser('{}' + ' ' * (total - 2)))
        self.assertIsNone(self.c.deser('{}' + ' ' * (total - 1)))
        self.assertIsNone(self.c.deser('1' * 129))

    def test_host_save_transfer_chunk_fits_limits(self):
        import base64
        data = base64.b64encode(b'x' * (256 * 1024)).decode('ascii')
        value = self.c.deser('{["id"]=1,["i"]=0,["n"]=100,["data"]="' + data + '"}')
        self.assertEqual(value['data'], data)

    def test_deterministic_malformed_corpus(self):
        rng = random.Random(40420)
        alphabet = '{}[]="\\.0123456789truefalsnil()+;'
        self.lua.execute('load=function() error("decoder compiled input") end; loadstring=load')
        for _ in range(300):
            value = ''.join(rng.choice(alphabet) for _ in range(rng.randint(0,100)))
            self.c.deser(value)  # must terminate and never raise


if __name__ == '__main__':
    unittest.main(verbosity=2)
