using Application.Contracts.Employees;
using Domain.Entities;
using Mapster;

namespace Application.Mapping;

public class MappingConfigration : IRegister
{
    public void Register(TypeAdapterConfig config)
    {

        //config.NewConfig<RegisterRequest, ApplicataionUser>()
        //    .Map(des => des.UserName, src => $"{src.FirstName}{src.LastName}");


        config.NewConfig<HousingResponse, Employees>()
            .Map(des => des, src => src.Employees);


        config.NewConfig<EmpolyeeResponse, Employees>()
            .Map(des => des.NameAR, src => src.NameAR)
            .Map(des => des.NameEN, src => src.NameEN);

        //config.NewConfig<(ApplicataionUser user, IList<string> userroles), UserResponse>()
        //    .Map(des => des, src => src.user)
        //    .Map(des => des.Roles, src => src.userroles);


        //config.NewConfig<Employees, EmpolyeeResponse>
        //        ()
        //        .Map(dest => dest.IBAN, src => src.IBAN)
        //        .Map(dest => dest.NameEN, src => src.NameEN)
        //        .Map(dest => dest.NameAR, src => src.NameAR);


    }
}
