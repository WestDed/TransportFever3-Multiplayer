"""Real upstream pure Lua, Lua 5.2; no game, native hooks or network exercised."""
from pathlib import Path
import unittest
from lupa.lua52 import LuaRuntime

ROOT = Path(__file__).resolve().parents[1]
CONTENT = ROOT / 'mod/mpfever_1/content'


class CompaniesOffline(unittest.TestCase):
    def setUp(self):
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        self.lua.globals().CO = self.lua.execute((CONTENT / 'mpfever_co.lua').read_text(encoding='utf-8'))
        self.lua.execute('reg = CO.new("separate", 7, 2000000)')

    def check(self, code):
        self.lua.execute(code)

    def test_host_claims_existing_company(self):
        self.check('local c,n=CO.join(reg,"host","Host",true,nil,"A"); assert(c.id==1 and c.ent==7 and not n)')

    def test_client_gets_independent_company(self):
        self.check('local c,n=CO.join(reg,"guest","Guest",false,nil,"B"); assert(n and c.id==2 and c.ent==nil and #reg.list==2)')

    def test_reconnect_is_idempotent(self):
        self.check('local a=CO.join(reg,"guest","Guest",false,nil,"B"); local b,n=CO.join(reg,"guest","Guest",false,nil,"B"); assert(a==b and not n and #reg.list==2)')

    def test_renamed_account_reclaims_company(self):
        self.check('local a=CO.join(reg,"old","Guest",false,nil,"B"); local b,n=CO.join(reg,"new","Guest",false,nil,"B"); assert(a==b and b.key=="new" and not n)')

    def test_different_accounts_same_name_separate(self):
        self.check('local a=CO.join(reg,"guest","Guest",false,nil,"B"); local b,n=CO.join(reg,"guest","Guest",false,nil,"C"); assert(a.id~=b.id and n and b.key=="guest@C")')

    def test_missing_account_uses_name_fallback(self):
        # Records current behaviour; this is not secure authentication.
        self.check('local a=CO.join(reg,"guest","Guest",false,nil,"B"); local b,n=CO.join(reg,"guest","Guest",false,nil,nil); assert(a==b and not n)')

    def test_ambiguous_account_does_not_pick_random_company(self):
        self.check('CO.join(reg,"one","One",false,nil,"B"); CO.join(reg,"two","Two",false,nil,"C"); reg.list[3].sid="B"; assert(CO.match(reg,"new","B",false)==nil)')

    def test_entity_mapping_local(self):
        self.check('local c=CO.join(reg,"guest","Guest",false); c.ent=900; assert(CO.entityOf(reg,2)==900 and CO.idOfEntity(reg,900)==2 and CO.entityOf(reg,99)==nil)')

    def test_signature_ignores_local_entity_ids(self):
        self.check('local before=CO.signature(reg); reg.list[1].ent=999; assert(CO.signature(reg)==before)')

    def test_serialized_registry_reconnect(self):
        self.lua.globals().C = self.lua.execute((CONTENT / 'mpfever_common.lua').read_text(encoding='utf-8'))
        self.check('CO.join(reg,"guest","Guest",false,nil,"B"); reg=C.deser(C.ser(reg)); local c,n=CO.join(reg,"guest","Guest",false,nil,"B"); assert(c.id==2 and not n and #reg.list==2)')

    def test_palette_unique_first_twelve(self):
        self.check('local used={[1]=true}; for i=2,12 do local c=CO.join(reg,"p"..i,"P",false); assert(not used[c.color]); used[c.color]=true end')

    def test_collision_reconnect_should_not_duplicate_company(self):
        # The unique Steam-account fallback correctly resolves this collision.
        self.check('CO.join(reg,"guest","Guest",false,nil,"B"); local a=CO.join(reg,"guest","Guest",false,nil,"C"); local b,n=CO.join(reg,"guest","Guest",false,nil,"C"); assert(a==b and not n and #reg.list==3)')

    @unittest.expectedFailure
    def test_signature_should_detect_account_binding_change(self):
        # AUD-04: identity sid is omitted from CO.signature.
        self.check('reg.list[1].sid="A"; local before=CO.signature(reg); reg.list[1].sid="B"; assert(CO.signature(reg)~=before)')


class CommonOffline(unittest.TestCase):
    def setUp(self):
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        self.lua.globals().C = self.lua.execute((CONTENT / 'mpfever_common.lua').read_text(encoding='utf-8'))

    def test_roundtrip_special_characters_and_nested_data(self):
        self.lua.execute('local x={s="Привет"..string.char(9,10,13,34,92), n=42, b=true, nested={1,2}}; local y=C.deser(C.ser(x)); assert(y.s==x.s and y.n==42 and y.b and y.nested[2]==2)')

    def test_malformed_literal_returns_nil(self):
        self.assertIsNone(self.lua.eval('C.deser("{")'))

    def test_decoder_should_accept_data_only(self):
        # AUD-02: expression payload must be rejected without execution.
        self.assertIsNone(self.lua.eval('C.deser("(function() return 7 end)()")'))


if __name__ == '__main__':
    unittest.main(verbosity=2)
