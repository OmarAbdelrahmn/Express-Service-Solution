using Mapster;

namespace Application.Mapping;

public class MappingConfigration : IRegister
{
    public void Register(TypeAdapterConfig config)
    {

        //config.NewConfig<RegisterRequest, ApplicataionUser>()
        //    .Map(des => des.UserName, src => $"{src.FirstName}{src.LastName}");


        //config.NewConfig<RegisterRequest, ApplicataionUser>()
        //    .Map(des => des.UserName, src => src.Email);

        //config.NewConfig<(ApplicataionUser user, IList<string> userroles), UserResponse>()
        //    .Map(des => des, src => src.user)
        //    .Map(des => des.Roles, src => src.userroles);
    }
}
